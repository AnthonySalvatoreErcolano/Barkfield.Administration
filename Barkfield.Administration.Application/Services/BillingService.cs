using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Deliveries;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Billing.Models;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// Charging deliveries through Square.
/// </summary>
/// <remarks>
/// <para>
/// The sequence is fixed and the order matters: pick the card, build the Square order, <b>persist
/// its id and the attempt number</b>, then charge. Persisting before the call is what makes a
/// crash mid-charge recoverable — Square refuses a reused idempotency key whose payload has
/// changed, so an attempt number that was never saved would leave the delivery permanently
/// unchargeable.
/// </para>
/// <para>
/// A refused card is an outcome, not an exception. It is recorded on the delivery and reported, so
/// the batch carries on and staff get one list of customers to ring rather than a stack trace.
/// </para>
/// </remarks>
public class BillingService
{
    private readonly IDeliveryQueries _deliveryQueries;
    private readonly IDeliveryCommands _deliveryCommands;
    private readonly ICustomerQueries _customerQueries;
    private readonly IProductQueries _productQueries;
    private readonly ISquareBillingService _squareBilling;
    private readonly ISquareCatalogService _squareCatalog;
    private readonly IBillingSettings _settings;
    private readonly ILogger<BillingService> _logger;

    public BillingService(
        IDeliveryQueries deliveryQueries,
        IDeliveryCommands deliveryCommands,
        ICustomerQueries customerQueries,
        IProductQueries productQueries,
        ISquareBillingService squareBilling,
        ISquareCatalogService squareCatalog,
        IBillingSettings settings,
        ILogger<BillingService> logger)
    {
        _deliveryQueries = deliveryQueries;
        _deliveryCommands = deliveryCommands;
        _customerQueries = customerQueries;
        _productQueries = productQueries;
        _squareBilling = squareBilling;
        _squareCatalog = squareCatalog;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Replaces the discounts staff have chosen for a delivery.
    /// </summary>
    /// <remarks>
    /// Validated against Square's live discount list, so a deleted or mistyped id is caught here
    /// rather than at the moment somebody presses charge. The name and rate are snapshotted for
    /// display; only the ids are ever sent.
    /// </remarks>
    public async Task SelectDiscountsAsync(
        Guid deliveryId,
        IReadOnlyCollection<string> squareDiscountIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(squareDiscountIds);

        DeliveryDetailDto dto = await LoadAsync(deliveryId, cancellationToken);

        var selected = new List<(string, string, string?, decimal?, decimal?)>();

        if (squareDiscountIds.Count > 0)
        {
            IReadOnlyCollection<SquareDiscountDto> available =
                await _squareCatalog.ListDiscountsAsync(cancellationToken);

            foreach (string id in squareDiscountIds.Distinct(StringComparer.Ordinal))
            {
                SquareDiscountDto discount = available.FirstOrDefault(d => d.Id == id)
                    ?? throw new ValidationException($"Square has no discount with ID '{id}'.");

                selected.Add((discount.Id, discount.Name, discount.DiscountType, discount.Percentage, discount.Amount));
            }
        }

        Delivery delivery = DeliveryRehydrator.Rehydrate(dto);

        delivery.SelectDiscounts(selected);

        await SaveAsync(delivery, dto.Revision, cancellationToken);
    }

    /// <summary>
    /// Charges one delivery, returning what happened rather than throwing on a refused card.
    /// </summary>
    public async Task<DeliveryChargeResult> ChargeAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        DeliveryDetailDto dto = await LoadAsync(deliveryId, cancellationToken);

        return await ChargeOneAsync(dto, cancellationToken);
    }

    /// <summary>
    /// Charges every delivery on a date that is ready and unpaid.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The real action on delivery day is "run them all", so this is the primary path — clicking
    /// through thirty of them one at a time is the drudgery the application exists to remove.
    /// </para>
    /// <para>
    /// Deliveries that are not ready, already paid or have no card are skipped and named. One
    /// declined card does not stop the rest, and the result is a single list of who to ring.
    /// </para>
    /// <para>
    /// Deliberately <b>not</b> wrapped in one transaction. Each charge is a real movement of money
    /// that cannot be rolled back, so each delivery is saved as soon as its outcome is known —
    /// a transaction spanning the batch could roll back a record of money Square has already taken.
    /// </para>
    /// </remarks>
    public async Task<BatchChargeResult> ChargeDueAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default)
    {
        DateTime date = deliveryDate.Date;

        var filter = new DeliveryFilter(
            ScheduledFrom: date,
            ScheduledTo: date,
            IncludeClosed: false,
            PageSize: 200);

        var candidates = await _deliveryQueries.SearchAsync(filter, cancellationToken);

        var charged = new List<DeliveryChargeResult>();
        var skipped = new List<ChargeSkip>();

        foreach (DeliveryListItemDto candidate in candidates.Items)
        {
            if (candidate.PaymentStatus == PaymentStatus.Paid)
            {
                skipped.Add(new ChargeSkip(candidate.Id, candidate.CustomerName, "Already paid."));
                continue;
            }

            if (candidate.ProcurementStatus != ProcurementStatus.Ready)
            {
                skipped.Add(new ChargeSkip(candidate.Id, candidate.CustomerName,
                    $"Not ready to charge — procurement is {candidate.ProcurementStatusName}."));
                continue;
            }

            DeliveryDetailDto dto = await LoadAsync(candidate.Id, cancellationToken);

            charged.Add(await ChargeOneAsync(dto, cancellationToken));
        }

        return new BatchChargeResult(date, candidates.TotalCount, charged, skipped);
    }

    /// <summary>
    /// The deliveries someone has to do something about: a card was refused, or a paid delivery
    /// came up short and a refund is owed.
    /// </summary>
    /// <remarks>
    /// Refunds are made by hand in Square, so this list is the only thing making sure one gets
    /// noticed.
    /// </remarks>
    public async Task<IReadOnlyCollection<ChargeAttentionItem>> GetNeedsAttentionAsync(
        DateTime? from,
        DateTime? to,
        CancellationToken cancellationToken = default)
    {
        var filter = new DeliveryFilter(
            ScheduledFrom: from?.Date,
            ScheduledTo: to?.Date,
            IncludeClosed: true,
            PageSize: 200,
            SortBy: "scheduledFor");

        var all = await _deliveryQueries.SearchAsync(filter, cancellationToken);

        var items = new List<ChargeAttentionItem>();

        foreach (DeliveryListItemDto d in all.Items)
        {
            if (d.PaymentStatus == PaymentStatus.Failed)
            {
                items.Add(new ChargeAttentionItem(
                    d.Id, d.CustomerName, d.ScheduledFor, ChargeAttentionReason.PaymentFailed,
                    $"{d.PaymentFailureCode}: {d.PaymentFailureReason}".Trim(' ', ':'),
                    d.PaymentAttemptCount));

                continue;
            }

            // A paid delivery missing a line means a refund is owed. Needs the lines, so it is
            // only checked for deliveries that were actually charged.
            if (d.PaymentStatus == PaymentStatus.Paid && d.UnresolvedLineCount == 0)
            {
                DeliveryDetailDto detail = await LoadAsync(d.Id, cancellationToken);

                if (detail.NeedsRefundAttention)
                {
                    string shorted = string.Join(", ", detail.Lines
                        .Where(l => l.OrderStatus == LineOrderStatus.Shorted)
                        .Select(l => l.ProductName));

                    items.Add(new ChargeAttentionItem(
                        d.Id, d.CustomerName, d.ScheduledFor, ChargeAttentionReason.RefundOwed,
                        $"Paid, but shorted: {shorted}. Refund in Square.",
                        d.PaymentAttemptCount));
                }
            }
        }

        return items;
    }

    // --- Internals ---------------------------------------------------------

    /// <summary>
    /// One charge, start to finish.
    /// </summary>
    private async Task<DeliveryChargeResult> ChargeOneAsync(
        DeliveryDetailDto dto,
        CancellationToken cancellationToken)
    {
        Delivery delivery = DeliveryRehydrator.Rehydrate(dto);

        // Local checks first, so nothing reaches Square that was never going to work.
        if (!delivery.CanCharge)
        {
            return DeliveryChargeResult.NotAttempted(dto.Id, dto.CustomerName, DescribeWhyNotChargeable(delivery));
        }

        CustomerDetailDto customer = await _customerQueries.GetByIdAsync(dto.CustomerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{dto.CustomerId}' was not found.");

        if (string.IsNullOrWhiteSpace(customer.SquareCustomerId))
        {
            return DeliveryChargeResult.NotAttempted(dto.Id, customer.FullName,
                $"{customer.FullName} is not linked to a Square profile, so there is no card to charge.");
        }

        SquareCardDto? card = await _squareBilling.GetCardToChargeAsync(customer.SquareCustomerId, cancellationToken);

        if (card is null)
        {
            // Recorded as a failure, not just reported: this belongs on the needs-attention list
            // next to the declines, because it is the same job — get a card from the customer.
            delivery.BeginChargeAttempt();
            delivery.RecordPaymentFailed("NO_CARD_ON_FILE", $"{customer.FullName} has no usable card on file in Square.");

            await SaveAsync(delivery, dto.Revision, cancellationToken);

            return DeliveryChargeResult.Declined(dto.Id, customer.FullName,
                "NO_CARD_ON_FILE", $"{customer.FullName} has no usable card on file in Square.");
        }

        IReadOnlyCollection<SquareOrderLineDto> lines =
            await BuildLinesAsync(delivery, cancellationToken);

        if (lines.Count == 0)
        {
            return DeliveryChargeResult.NotAttempted(dto.Id, customer.FullName,
                "Nothing on this delivery can be billed — every line was shorted.");
        }

        // Claim the attempt and persist it BEFORE calling Square. If the process dies between
        // here and the response, the next attempt gets a fresh key rather than reusing one with
        // different contents, which Square refuses outright.
        int attempt = delivery.BeginChargeAttempt();

        SquareOrderResultDto order;

        try
        {
            // The order is reused across retries, so it is only built the first time.
            if (string.IsNullOrWhiteSpace(delivery.SquareOrderId))
            {
                order = await _squareBilling.CreateOrderAsync(
                    new SquareOrderRequestDto(
                        customer.SquareCustomerId,
                        delivery.Id.ToString(),
                        $"{delivery.Id}:order:{attempt}",
                        lines,
                        delivery.Discounts.Select(d => d.SquareDiscountId).ToList()),
                    cancellationToken);

                delivery.RecordSquareOrder(order.OrderId);
            }
            else
            {
                // A retry: keep the existing order, which a failed payment left open.
                order = new SquareOrderResultDto(delivery.SquareOrderId, dto.Total);
            }

            await SaveAsync(delivery, dto.Revision, cancellationToken);
        }
        catch (ExternalServiceException ex)
        {
            delivery.RecordPaymentFailed("ORDER_FAILED", ex.Message);

            await SaveAsync(delivery, dto.Revision, cancellationToken);

            _logger.LogError(ex, "Could not build a Square order for delivery {DeliveryId}.", delivery.Id);

            return DeliveryChargeResult.Failed(dto.Id, customer.FullName, "ORDER_FAILED", ex.Message);
        }

        SquareChargeResultDto result = await _squareBilling.ChargeAsync(
            customer.SquareCustomerId,
            card.Id,
            order.OrderId,
            order.TotalAmount,
            $"{delivery.Id}:pay:{attempt}",
            delivery.Id.ToString(),
            cancellationToken);

        // Reload the revision: the save above moved it on.
        int revision = (await _deliveryQueries.GetByIdAsync(delivery.Id, cancellationToken))?.Revision ?? dto.Revision;

        if (result.IsPaid)
        {
            delivery.RecordPaid(result.PaymentId!, result.AmountCharged ?? order.TotalAmount, result.ReceiptUrl);

            await SaveAsync(delivery, revision, cancellationToken);

            return DeliveryChargeResult.Paid(
                dto.Id, customer.FullName, result.AmountCharged ?? order.TotalAmount,
                result.PaymentId!, result.ReceiptUrl, card.Label);
        }

        delivery.RecordPaymentFailed(result.ErrorCode, result.ErrorDetail ?? "Square refused the payment.");

        await SaveAsync(delivery, revision, cancellationToken);

        return result.Outcome == SquareChargeOutcome.Declined
            ? DeliveryChargeResult.Declined(dto.Id, customer.FullName, result.ErrorCode, result.ErrorDetail)
            : DeliveryChargeResult.Failed(dto.Id, customer.FullName, result.ErrorCode, result.ErrorDetail);
    }

    /// <summary>
    /// Maps the delivery's billable lines onto Square catalog references.
    /// </summary>
    /// <remarks>
    /// A substituted line bills the substitute, and a shorted line is left out entirely. Prices are
    /// not sent: Square looks them up, which is why a product missing its Square id cannot be
    /// billed at all.
    /// </remarks>
    private async Task<IReadOnlyCollection<SquareOrderLineDto>> BuildLinesAsync(
        Delivery delivery,
        CancellationToken cancellationToken)
    {
        var productIds = delivery.ChargeableLines
            .Select(l => l.SubstitutedWithProductId ?? l.ProductId)
            .Distinct()
            .ToList();

        IReadOnlyCollection<ProductDto> products = await _productQueries.GetByIdsAsync(productIds, cancellationToken);

        var byId = products.ToDictionary(p => p.Id);

        var lines = new List<SquareOrderLineDto>();

        foreach (DeliveryLine line in delivery.ChargeableLines)
        {
            Guid productId = line.SubstitutedWithProductId ?? line.ProductId;

            if (!byId.TryGetValue(productId, out ProductDto? product)
                || string.IsNullOrWhiteSpace(product.SquareCatalogObjectId))
            {
                throw new ValidationException(
                    $"'{line.PackingNameOrProductName()}' has no Square catalog reference, so it cannot be billed.");
            }

            lines.Add(new SquareOrderLineDto(product.SquareCatalogObjectId, line.Quantity));
        }

        return lines;
    }

    private static string DescribeWhyNotChargeable(Delivery delivery)
    {
        if (delivery.PaymentStatus == PaymentStatus.Paid) return "Already paid.";
        if (delivery.IsClosed) return $"The delivery is {delivery.Status}.";

        if (delivery.ProcurementStatus != ProcurementStatus.Ready)
        {
            string outstanding = string.Join(", ",
                delivery.UnresolvedLines.Select(l => $"{l.ProductName} ({l.OrderStatus})"));

            return $"Not ready to charge. Outstanding: {outstanding}.";
        }

        return "Nothing on this delivery can be billed.";
    }

    private async Task<DeliveryDetailDto> LoadAsync(Guid deliveryId, CancellationToken cancellationToken) =>
        await _deliveryQueries.GetByIdAsync(deliveryId, cancellationToken)
            ?? throw new NotFoundException($"Delivery with ID '{deliveryId}' was not found.");

    private async Task SaveAsync(Delivery delivery, int expectedRevision, CancellationToken cancellationToken)
    {
        if (!await _deliveryCommands.SaveAsync(delivery, expectedRevision, cancellationToken))
        {
            throw new ConflictException(
                "This delivery was changed by someone else while you were saving. Reload and try again.");
        }
    }
}

/// <summary>Billing configuration the Application layer needs without seeing Infrastructure.</summary>
public interface IBillingSettings
{
    /// <summary>
    /// The discount applied by default to a delivery from a subscription, or null when none is
    /// configured. Every autoship customer gets it, so it is defaulted rather than remembered.
    /// </summary>
    string? AutoshipDiscountId { get; }
}

internal static class DeliveryLineExtensions
{
    /// <summary>The substitute's name if swapped, otherwise the original — for a message.</summary>
    public static string PackingNameOrProductName(this DeliveryLine line) =>
        line.SubstitutedWithProductName ?? line.ProductName;
}
