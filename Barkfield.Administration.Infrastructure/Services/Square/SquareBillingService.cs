using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square;
using Square.Cards;
using Square.Core;
using Square.Orders;
using Square.Payments;
using System.Globalization;

namespace Barkfield.Administration.Infrastructure.Services.Square;

/// <summary>
/// Charges cards on file through the Square .NET SDK (v47).
/// </summary>
/// <remarks>
/// <para>
/// Every behaviour relied on here was verified against the Square sandbox before this class was
/// written, because guessing at a payment API is how customers get charged twice.
/// </para>
/// <para>
/// <b>Money crosses the boundary here.</b> Square counts in minor units — 9504 is $95.04 — and the
/// rest of the application works in dollars.
/// </para>
/// <para>
/// <b>Line items are catalog references, never prices.</b> Square looks the price up, applies the
/// discounts it was given, adds tax and returns the total. Barkfield Road's catalog lives in Square
/// and is the source of truth for pricing, so the figure Square computes is the one on the receipt.
/// </para>
/// </remarks>
public class SquareBillingService : ISquareBillingService
{
    /// <summary>
    /// Square's error category for a card problem, as opposed to a problem with our request.
    /// </summary>
    /// <remarks>
    /// Verified against the sandbox: a declining test source returns
    /// <c>PAYMENT_METHOD_ERROR / GENERIC_DECLINE</c>, while a bad card id or catalog id returns
    /// <c>INVALID_REQUEST_ERROR / NOT_FOUND</c>. That distinction is the whole basis for deciding
    /// whether staff ring the customer or raise a bug.
    /// </remarks>
    private const string PaymentMethodErrorCategory = "PAYMENT_METHOD_ERROR";

    private readonly ISquareClient _squareClient;
    private readonly SquareSettings _settings;
    private readonly ILogger<SquareBillingService> _logger;

    public SquareBillingService(
        ISquareClient squareClient,
        IOptions<SquareSettings> settings,
        ILogger<SquareBillingService> logger)
    {
        _squareClient = squareClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<SquareCardDto?> GetCardToChargeAsync(
        string squareCustomerId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(squareCustomerId, nameof(squareCustomerId));

        try
        {
            Pager<global::Square.Card> pager = await _squareClient.Cards.ListAsync(
                new ListCardsRequest { CustomerId = squareCustomerId },
                cancellationToken: cancellationToken);

            var cards = new List<SquareCardDto>();

            await foreach (global::Square.Card card in pager.WithCancellation(cancellationToken))
            {
                if (string.IsNullOrWhiteSpace(card.Id)) continue;

                cards.Add(new SquareCardDto(
                    card.Id,
                    card.CardBrand?.ToString(),
                    card.Last4,
                    (int?)card.ExpMonth,
                    (int?)card.ExpYear,
                    card.Enabled ?? true,
                    ParseTimestamp(card.CreatedAt)));
            }

            // The newest usable card: the one the customer most recently handed over.
            return cards
                .Where(c => c.Enabled)
                .OrderByDescending(c => c.CreatedAt ?? DateTime.MinValue)
                .FirstOrDefault();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square ListCards failed for customer {SquareCustomerId}.", squareCustomerId);

            throw new ExternalServiceException("Square ListCards failed.", ex);
        }
    }

    public async Task<SquareOrderResultDto> CreateOrderAsync(
        SquareOrderRequestDto request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Lines.Count == 0)
        {
            throw new ExternalServiceException(
                "A Square order needs at least one line.",
                new InvalidOperationException("No lines supplied."));
        }

        var order = new global::Square.Order
        {
            LocationId = RequireLocationId(),
            CustomerId = request.SquareCustomerId,

            // Our delivery id, so a Square receipt or a bank statement can be traced back here.
            ReferenceId = request.ReferenceId,

            LineItems = request.Lines
                .Select(l => new global::Square.OrderLineItem
                {
                    CatalogObjectId = l.SquareVariationId,

                    // Square takes quantity as a string; it supports fractional units elsewhere.
                    Quantity = l.Quantity.ToString(CultureInfo.InvariantCulture)
                })
                .ToList(),

            // Order-scoped, so each discount applies across the whole delivery rather than
            // needing to be attached to individual lines.
            Discounts = request.SquareDiscountIds
                .Select(id => new global::Square.OrderLineItemDiscount
                {
                    CatalogObjectId = id,
                    Scope = global::Square.OrderLineItemDiscountScope.Order
                })
                .ToList()
        };

        try
        {
            CreateOrderResponse response = await _squareClient.Orders.CreateAsync(
                new CreateOrderRequest { IdempotencyKey = request.IdempotencyKey, Order = order },
                cancellationToken: cancellationToken);

            ThrowIfErrors(response.Errors, "CreateOrder");

            if (string.IsNullOrWhiteSpace(response.Order?.Id))
            {
                throw new ExternalServiceException(
                    "Square created an order but returned no id.",
                    new InvalidOperationException("Empty order id."));
            }

            long? total = response.Order.TotalMoney?.Amount;

            return new SquareOrderResultDto(response.Order.Id, ToDollars(total) ?? 0m);
        }
        catch (ExternalServiceException)
        {
            throw;
        }
        catch (SquareApiException ex)
        {
            string detail = FormatErrors(ex);

            _logger.LogError(ex, "Square CreateOrder failed for delivery {ReferenceId}: {Detail}",
                request.ReferenceId, detail);

            throw new ExternalServiceException($"Square CreateOrder failed: {detail}", ex);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square CreateOrder failed for delivery {ReferenceId}.", request.ReferenceId);

            throw new ExternalServiceException("Square CreateOrder failed.", ex);
        }
    }

    public async Task<SquareChargeResultDto> ChargeAsync(
        string squareCustomerId,
        string squareCardId,
        string squareOrderId,
        decimal amount,
        string idempotencyKey,
        string referenceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(squareCustomerId, nameof(squareCustomerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(squareCardId, nameof(squareCardId));
        ArgumentException.ThrowIfNullOrWhiteSpace(squareOrderId, nameof(squareOrderId));
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey, nameof(idempotencyKey));

        var request = new CreatePaymentRequest
        {
            IdempotencyKey = idempotencyKey,
            SourceId = squareCardId,
            CustomerId = squareCustomerId,
            LocationId = RequireLocationId(),
            OrderId = squareOrderId,
            AmountMoney = new global::Square.Money
            {
                Amount = ToMinorUnits(amount),
                Currency = global::Square.Currency.Usd
            },
            ReferenceId = referenceId
        };

        try
        {
            CreatePaymentResponse response = await _squareClient.Payments.CreateAsync(
                request, cancellationToken: cancellationToken);

            ThrowIfErrors(response.Errors, "CreatePayment");

            global::Square.Payment? payment = response.Payment;

            if (payment is null || string.IsNullOrWhiteSpace(payment.Id))
            {
                return new SquareChargeResultDto(
                    SquareChargeOutcome.Failed,
                    ErrorCode: "NO_PAYMENT_RETURNED",
                    ErrorDetail: "Square accepted the request but returned no payment.");
            }

            // COMPLETED is the money taken. APPROVED means authorised but not captured, which
            // this flow never asks for — treating it as paid would report money we do not have.
            if (!string.Equals(payment.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "Square payment {PaymentId} for delivery {ReferenceId} came back as {Status}, not COMPLETED.",
                    payment.Id, referenceId, payment.Status);

                return new SquareChargeResultDto(
                    SquareChargeOutcome.Failed,
                    PaymentId: payment.Id,
                    ErrorCode: payment.Status,
                    ErrorDetail: $"Square returned payment status '{payment.Status}' rather than COMPLETED.");
            }

            return new SquareChargeResultDto(
                SquareChargeOutcome.Paid,
                PaymentId: payment.Id,
                AmountCharged: ToDollars(payment.AmountMoney?.Amount),
                ReceiptUrl: payment.ReceiptUrl);
        }
        catch (SquareApiException ex)
        {
            // A refused card is an outcome, not a fault, so it is returned rather than thrown.
            // Anything else is our request being wrong, and must not send staff chasing a customer.
            bool declined = ex.Errors?.Any(e =>
                string.Equals(e.Category.ToString(), PaymentMethodErrorCategory, StringComparison.OrdinalIgnoreCase)) == true;

            global::Square.Error? first = ex.Errors?.FirstOrDefault();

            if (declined)
            {
                _logger.LogInformation(
                    "Square declined the card for delivery {ReferenceId}: {Code} {Detail}",
                    referenceId, first?.Code.ToString(), first?.Detail);

                return new SquareChargeResultDto(
                    SquareChargeOutcome.Declined,
                    ErrorCode: first?.Code.ToString(),
                    ErrorDetail: first?.Detail);
            }

            _logger.LogError(ex, "Square CreatePayment failed for delivery {ReferenceId}: {Detail}",
                referenceId, FormatErrors(ex));

            return new SquareChargeResultDto(
                SquareChargeOutcome.Failed,
                ErrorCode: first is null ? ex.StatusCode.ToString(CultureInfo.InvariantCulture) : first.Code.ToString(),
                ErrorDetail: first?.Detail ?? "Square rejected the payment request.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Square CreatePayment failed for delivery {ReferenceId}.", referenceId);

            return new SquareChargeResultDto(
                SquareChargeOutcome.Failed,
                ErrorCode: "UNREACHABLE",
                ErrorDetail: "Square could not be reached.");
        }
    }

    /// <summary>
    /// The location every order and payment is booked against.
    /// </summary>
    /// <remarks>
    /// Configured rather than discovered: the id differs between sandbox and production, and a
    /// payment booked to the wrong location would land in the wrong set of books.
    /// </remarks>
    private string RequireLocationId()
    {
        if (string.IsNullOrWhiteSpace(_settings.LocationId))
        {
            throw new ExternalServiceException(
                "Square:LocationId is not configured, so nothing can be billed.",
                new InvalidOperationException("Missing Square location id."));
        }

        return _settings.LocationId.Trim();
    }

    /// <summary>Square counts in minor units: $95.04 is 9504.</summary>
    private static long ToMinorUnits(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);

    private static decimal? ToDollars(long? minorUnits) => minorUnits is null ? null : minorUnits.Value / 100m;

    /// <summary>Square timestamps arrive as ISO 8601 strings.</summary>
    private static DateTime? ParseTimestamp(string? value) =>
        DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out DateTime parsed)
            ? parsed
            : null;

    private static string FormatErrors(SquareApiException ex) =>
        ex.Errors is null || ex.Errors.Count == 0
            ? $"status {ex.StatusCode}"
            : string.Join("; ", ex.Errors.Select(e => $"{e.Category}/{e.Code}: {e.Detail}"));

    /// <summary>
    /// Square can answer 200 with an Errors collection, so a successful status code is not on its
    /// own a successful call.
    /// </summary>
    private void ThrowIfErrors(IEnumerable<global::Square.Error>? errors, string operation)
    {
        if (errors is null || !errors.Any()) return;

        string detail = string.Join("; ", errors.Select(e => $"{e.Category}/{e.Code}: {e.Detail}"));

        _logger.LogError("Square {Operation} returned errors: {Errors}", operation, detail);

        throw new ExternalServiceException(
            $"Square {operation} returned errors: {detail}",
            new InvalidOperationException(detail));
    }
}
