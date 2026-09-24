using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Deliveries;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Deliveries.Models;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// Deliveries: turning due subscriptions into dispatches, procuring the stock, packing, and
/// recording what happened.
/// </summary>
/// <remarks>
/// <para>
/// Every mutation is load, call one domain method, save. <see cref="Delivery"/> owns the rules —
/// what can be packed, what a substitution does to the total, when contents are frozen — and
/// routing every change through it keeps those rules off the call sites.
/// </para>
/// <para>
/// Two operations span aggregates and therefore run inside a transaction scope: marking a
/// delivery delivered advances the subscription's rotation and expires its add-ons, and
/// cancelling one rolls the subscription's next date forward. Half of either would leave the
/// customer's standing order describing something that never happened.
/// </para>
/// </remarks>
public class DeliveryService
{
    private readonly IDeliveryQueries _deliveryQueries;
    private readonly IDeliveryCommands _deliveryCommands;
    private readonly ISubscriptionQueries _subscriptionQueries;
    private readonly ISubscriptionCommands _subscriptionCommands;
    private readonly ICustomerQueries _customerQueries;
    private readonly IProductQueries _productQueries;
    private readonly ITransactionScopeFactory _transactions;

    public DeliveryService(
        IDeliveryQueries deliveryQueries,
        IDeliveryCommands deliveryCommands,
        ISubscriptionQueries subscriptionQueries,
        ISubscriptionCommands subscriptionCommands,
        ICustomerQueries customerQueries,
        IProductQueries productQueries,
        ITransactionScopeFactory transactions)
    {
        _deliveryQueries = deliveryQueries;
        _deliveryCommands = deliveryCommands;
        _subscriptionQueries = subscriptionQueries;
        _subscriptionCommands = subscriptionCommands;
        _customerQueries = customerQueries;
        _productQueries = productQueries;
        _transactions = transactions;
    }

    // --- Reads -------------------------------------------------------------

    public Task<PagedResult<DeliveryListItemDto>> SearchAsync(
        DeliveryFilter filter,
        CancellationToken cancellationToken = default) =>
        _deliveryQueries.SearchAsync(filter, cancellationToken);

    public async Task<DeliveryDetailDto> GetAsync(Guid deliveryId, CancellationToken cancellationToken = default) =>
        await _deliveryQueries.GetByIdAsync(deliveryId, cancellationToken)
            ?? throw new NotFoundException($"Delivery with ID '{deliveryId}' was not found.");

    /// <summary>
    /// The printable sheet for a date range.
    /// </summary>
    /// <remarks>
    /// A range because the prep day covers more than one delivery day — generation is per day,
    /// but the person walking the stockroom wants everything they are gathering for.
    /// </remarks>
    public async Task<IReadOnlyCollection<DeliverySheetDto>> GetSheetAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        if (to.Date < from.Date)
        {
            throw new ValidationException("The end of the range cannot be before its start.");
        }

        if ((to.Date - from.Date).TotalDays > 62)
        {
            throw new ValidationException("A delivery sheet can cover at most 62 days.");
        }

        return await _deliveryQueries.GetSheetAsync(from.Date, to.Date, cancellationToken);
    }

    /// <summary>What generation would do for a date, without doing it.</summary>
    public Task<IReadOnlyCollection<DueSubscriptionDto>> GetDueAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default) =>
        _deliveryQueries.GetDueSubscriptionsAsync(deliveryDate.Date, cancellationToken);

    // --- Generation --------------------------------------------------------

    /// <summary>
    /// Creates deliveries for every subscription due on a date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One date per run, triggered by staff. Nothing in this application wakes up to do it, and
    /// nothing should: the person generating Thursday's deliveries on Tuesday is the person who
    /// then has to gather the stock.
    /// </para>
    /// <para>
    /// Subscriptions whose dated pause has come round are resumed here, which is the moment that
    /// date takes effect. A dated pause resumes <i>to</i> its date, so the customer comes back on
    /// the day they said they would.
    /// </para>
    /// <para>
    /// Partial success is normal. A customer with no address does not stop the rest of the day
    /// being created — they are reported and skipped. The whole run is one transaction, so a
    /// genuine failure leaves no half-generated day behind.
    /// </para>
    /// </remarks>
    public async Task<DeliveryGenerationResult> GenerateAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default)
    {
        DateTime date = deliveryDate.Date;

        // Generation dates the deliveries it creates, and a delivery cannot be scheduled into
        // the past. Catching up a missed day means generating for today, which picks up
        // everything overdue anyway.
        if (date < DateTime.UtcNow.Date)
        {
            throw new ValidationException("Deliveries cannot be generated for a date in the past.");
        }

        IReadOnlyCollection<DueSubscriptionDto> due =
            await _deliveryQueries.GetDueSubscriptionsAsync(date, cancellationToken);

        var created = new List<Guid>();
        var resumed = new List<string>();
        var skipped = new List<GenerationSkip>();

        foreach (DueSubscriptionDto candidate in due.Where(d => !d.WillGenerate))
        {
            skipped.Add(new GenerationSkip(
                candidate.SubscriptionId,
                candidate.SubscriptionName ?? "(unnamed)",
                candidate.CustomerName,
                candidate.SkipReason!));
        }

        var toGenerate = due.Where(d => d.WillGenerate).ToList();

        if (toGenerate.Count == 0)
        {
            return new DeliveryGenerationResult(date, due.Count, created, resumed, skipped);
        }

        await using ITransactionScope transaction = await _transactions.BeginAsync(cancellationToken);

        foreach (DueSubscriptionDto candidate in toGenerate)
        {
            SubscriptionDetailDto? subscriptionDto =
                await _subscriptionQueries.GetByIdAsync(candidate.SubscriptionId, cancellationToken);

            CustomerDetailDto? customerDto =
                await _customerQueries.GetByIdAsync(candidate.CustomerId, cancellationToken);

            if (subscriptionDto is null || customerDto is null)
            {
                skipped.Add(new GenerationSkip(
                    candidate.SubscriptionId,
                    candidate.SubscriptionName ?? "(unnamed)",
                    candidate.CustomerName,
                    "The subscription or customer disappeared while generating."));
                continue;
            }

            Subscription subscription = SubscriptionRehydrator.Rehydrate(subscriptionDto);
            Customer customer = CustomerRehydrator.Rehydrate(customerDto);

            // A dated pause that has come round. Resume sets the next delivery to the return
            // date, so the manifest below is built for the day the customer expects.
            if (subscription.Status == SubscriptionStatus.Paused)
            {
                subscription.Resume();
                resumed.Add($"{customer.FullName} — {subscription.DisplayName}");
            }

            // The delivery is dated by the run, not by whatever stale date the subscription held.
            // Overdue subscriptions are being caught up, and they ship on the day being generated.
            if (subscription.NextDeliveryDate.Date != date)
            {
                subscription.Reschedule(date);
            }

            DeliveryManifest manifest = subscription.BuildNextDeliveryManifest();

            if (manifest.IsEmpty)
            {
                skipped.Add(new GenerationSkip(
                    candidate.SubscriptionId,
                    subscription.DisplayName,
                    customer.FullName,
                    "Nothing is scheduled to ship."));
                continue;
            }

            IReadOnlyDictionary<Guid, Product> catalog =
                await LoadCatalogAsync(manifest.Lines.Select(l => l.ProductId), cancellationToken);

            Delivery delivery;

            try
            {
                delivery = Delivery.Schedule(
                    subscription.Id, manifest, subscription.FulfillmentMethod, catalog, customer);
            }
            catch (DomainException ex)
            {
                // A broken rule for one subscription is that subscription's problem, not the
                // day's. Reported and skipped rather than abandoning everything created so far.
                skipped.Add(new GenerationSkip(
                    candidate.SubscriptionId, subscription.DisplayName, customer.FullName, ex.Message));
                continue;
            }

            await _deliveryCommands.CreateAsync(delivery, cancellationToken);

            // The subscription's own date and any resume have to land with the delivery, or the
            // next run would generate the same thing again.
            if (!await _subscriptionCommands.SaveAsync(subscription, subscriptionDto.Revision, cancellationToken))
            {
                throw new ConflictException(
                    $"{customer.FullName}'s subscription was changed while deliveries were being generated. "
                    + "Nothing was created — reload and try again.");
            }

            created.Add(delivery.Id);
        }

        await transaction.CommitAsync(cancellationToken);

        return new DeliveryGenerationResult(date, due.Count, created, resumed, skipped);
    }

    /// <summary>
    /// Creates a one-off delivery for a customer already in the system, on a day they are not
    /// otherwise scheduled, without touching their subscription.
    /// </summary>
    public async Task<Guid> CreateOneOffAsync(
        Guid customerId,
        DateTime scheduledFor,
        FulfillmentMethod fulfillmentMethod,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> lines,
        string? notes,
        CancellationToken cancellationToken = default)
    {
        if (lines is null || lines.Count == 0)
        {
            throw new ValidationException("A one-off delivery needs at least one product.");
        }

        if (scheduledFor.Date < DateTime.UtcNow.Date)
        {
            throw new ValidationException("A delivery cannot be scheduled in the past.");
        }

        CustomerDetailDto customerDto = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        if (!customerDto.IsActive)
        {
            throw new ValidationException($"{customerDto.FullName} is deactivated and cannot take a delivery.");
        }

        IReadOnlyDictionary<Guid, Product> catalog =
            await LoadCatalogAsync(lines.Select(l => l.ProductId), cancellationToken, requireActive: true);

        Customer customer = CustomerRehydrator.Rehydrate(customerDto);

        Delivery delivery = Delivery.ScheduleOneOff(customer, scheduledFor, fulfillmentMethod, lines, catalog);

        if (!string.IsNullOrWhiteSpace(notes)) delivery.UpdateNotes(notes);

        await _deliveryCommands.CreateAsync(delivery, cancellationToken);

        return delivery.Id;
    }

    // --- Contents ----------------------------------------------------------

    public async Task AddLineAsync(
        Guid deliveryId,
        Guid productId,
        int quantity,
        CancellationToken cancellationToken = default)
    {
        Product product = await LoadProductAsync(productId, cancellationToken, requireActive: true);

        await MutateAsync(deliveryId, delivery => delivery.AddLine(product, quantity), cancellationToken);
    }

    public Task ChangeLineQuantityAsync(
        Guid deliveryId,
        Guid lineId,
        int quantity,
        CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.ChangeLineQuantity(lineId, quantity), cancellationToken);

    public Task RemoveLineAsync(Guid deliveryId, Guid lineId, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.RemoveLine(lineId), cancellationToken);

    // --- Procurement -------------------------------------------------------

    public Task MarkLineOrderedAsync(Guid deliveryId, Guid lineId, string? note, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.MarkLineOrdered(lineId, note), cancellationToken);

    /// <summary>
    /// Records stock physically received and set aside. Omit the quantity for the full line.
    /// </summary>
    public Task ReceiveLineAsync(
        Guid deliveryId,
        Guid lineId,
        int? quantityReceived,
        string? note,
        CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d =>
        {
            if (quantityReceived is null) d.MarkLineReceived(lineId, note);
            else d.ReceiveLineQuantity(lineId, quantityReceived.Value, note);
        }, cancellationToken);

    public Task MarkLineOutOfStockAsync(Guid deliveryId, Guid lineId, string? note, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.MarkLineOutOfStock(lineId, note), cancellationToken);

    public async Task SubstituteLineAsync(
        Guid deliveryId,
        Guid lineId,
        Guid substituteProductId,
        string? note,
        CancellationToken cancellationToken = default)
    {
        Product substitute = await LoadProductAsync(substituteProductId, cancellationToken, requireActive: true);

        await MutateAsync(deliveryId, d => d.SubstituteLine(lineId, substitute, note), cancellationToken);
    }

    /// <summary>Ships without the line, knowingly. Resolves an out-of-stock.</summary>
    public Task ShortLineAsync(Guid deliveryId, Guid lineId, string? note, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.ShortLine(lineId, note), cancellationToken);

    public Task ResetLineAsync(Guid deliveryId, Guid lineId, string? note, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.ResetLineProcurement(lineId, note), cancellationToken);

    /// <summary>Puts every unresolved line on order — placing a PO is one act.</summary>
    public Task MarkAllOrderedAsync(Guid deliveryId, string? note, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.MarkAllLinesOrdered(note), cancellationToken);

    // --- Workflow ----------------------------------------------------------

    /// <summary>Refused unless every line is resolved; the domain names what is outstanding.</summary>
    public Task MarkPackedAsync(Guid deliveryId, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.MarkPacked(), cancellationToken);

    public Task MarkFailedAsync(Guid deliveryId, string reason, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.MarkFailed(reason), cancellationToken);

    public Task UpdateNotesAsync(Guid deliveryId, string? notes, CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.UpdateNotes(notes), cancellationToken);

    public Task SetRequestedWindowAsync(
        Guid deliveryId,
        TimeOnly? start,
        TimeOnly? end,
        CancellationToken cancellationToken = default)
    {
        TimeWindow? window = null;

        if (start is not null || end is not null)
        {
            if (start is null || end is null)
            {
                throw new ValidationException("A delivery window needs both a start and an end time.");
            }

            window = TimeWindow.Create(start.Value, end.Value);
        }

        return MutateAsync(deliveryId, d => d.SetRequestedWindow(window), cancellationToken);
    }

    public Task SetServiceDurationOverrideAsync(
        Guid deliveryId,
        int? minutes,
        CancellationToken cancellationToken = default) =>
        MutateAsync(deliveryId, d => d.SetServiceDurationOverride(minutes), cancellationToken);

    /// <summary>
    /// Records a completed delivery and advances the subscription behind it, together.
    /// </summary>
    /// <remarks>
    /// This is the reason the transaction scope exists. <see cref="Subscription.CompleteDelivery"/>
    /// advances every rotation, consumes pending add-ons and rolls the dates forward — if that
    /// landed without the delivery, or the delivery without it, the customer's standing order
    /// would describe a dispatch that never happened, or repeat one that did.
    /// </remarks>
    public async Task MarkDeliveredAsync(
        Guid deliveryId,
        DateTime deliveredOn,
        CancellationToken cancellationToken = default)
    {
        DeliveryDetailDto dto = await GetAsync(deliveryId, cancellationToken);
        Delivery delivery = DeliveryRehydrator.Rehydrate(dto);

        SubscriptionDetailDto? subscriptionDto = dto.SubscriptionId is null
            ? null
            : await _subscriptionQueries.GetByIdAsync(dto.SubscriptionId.Value, cancellationToken);

        await using ITransactionScope transaction = await _transactions.BeginAsync(cancellationToken);

        delivery.MarkDelivered(deliveredOn);

        if (!await _deliveryCommands.SaveAsync(delivery, dto.Revision, cancellationToken))
        {
            throw new ConflictException(
                "This delivery was changed by someone else while you were saving. Reload and try again.");
        }

        if (subscriptionDto is not null)
        {
            Subscription subscription = SubscriptionRehydrator.Rehydrate(subscriptionDto);

            subscription.CompleteDelivery(deliveredOn);

            if (!await _subscriptionCommands.SaveAsync(subscription, subscriptionDto.Revision, cancellationToken))
            {
                throw new ConflictException(
                    "The subscription behind this delivery was changed while you were saving. "
                    + "Nothing was recorded — reload and try again.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>
    /// Cancels a delivery and rolls its subscription's next date forward, together.
    /// </summary>
    /// <remarks>
    /// Without the roll-forward the subscription would still point at a day whose delivery has
    /// been cancelled, and someone would have to remember to skip it by hand.
    ///
    /// The roll only happens when the subscription still points at the cancelled date. If it has
    /// already moved on — rescheduled, or a later delivery generated — rolling again would skip a
    /// delivery nobody asked to skip.
    /// </remarks>
    public async Task CancelAsync(Guid deliveryId, CancellationToken cancellationToken = default)
    {
        DeliveryDetailDto dto = await GetAsync(deliveryId, cancellationToken);
        Delivery delivery = DeliveryRehydrator.Rehydrate(dto);

        SubscriptionDetailDto? subscriptionDto = dto.SubscriptionId is null
            ? null
            : await _subscriptionQueries.GetByIdAsync(dto.SubscriptionId.Value, cancellationToken);

        bool shouldRoll = subscriptionDto is not null
            && subscriptionDto.NextDeliveryDate.Date == dto.ScheduledFor.Date
            && subscriptionDto.Status != SubscriptionStatus.Canceled;

        await using ITransactionScope transaction = await _transactions.BeginAsync(cancellationToken);

        delivery.Cancel();

        if (!await _deliveryCommands.SaveAsync(delivery, dto.Revision, cancellationToken))
        {
            throw new ConflictException(
                "This delivery was changed by someone else while you were saving. Reload and try again.");
        }

        if (shouldRoll)
        {
            Subscription subscription = SubscriptionRehydrator.Rehydrate(subscriptionDto!);

            subscription.SkipNextDelivery();

            if (!await _subscriptionCommands.SaveAsync(subscription, subscriptionDto!.Revision, cancellationToken))
            {
                throw new ConflictException(
                    "The subscription behind this delivery was changed while you were saving. "
                    + "Nothing was cancelled — reload and try again.");
            }
        }

        await transaction.CommitAsync(cancellationToken);
    }

    // --- Internals ---------------------------------------------------------

    private async Task MutateAsync(
        Guid deliveryId,
        Action<Delivery> mutate,
        CancellationToken cancellationToken)
    {
        DeliveryDetailDto dto = await GetAsync(deliveryId, cancellationToken);

        Delivery delivery = DeliveryRehydrator.Rehydrate(dto);

        mutate(delivery);

        if (!await _deliveryCommands.SaveAsync(delivery, dto.Revision, cancellationToken))
        {
            throw new ConflictException(
                "This delivery was changed by someone else while you were saving. Reload and try again.");
        }
    }

    private async Task<Product> LoadProductAsync(
        Guid productId,
        CancellationToken cancellationToken,
        bool requireActive)
    {
        ProductDto dto = await _productQueries.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{productId}' was not found.");

        if (requireActive && !dto.IsActive)
        {
            throw new ValidationException($"'{dto.Name}' has been discontinued and cannot be added to a delivery.");
        }

        return ToProduct(dto);
    }

    /// <summary>
    /// Builds the product dictionary a delivery needs to snapshot names and prices.
    /// </summary>
    private async Task<IReadOnlyDictionary<Guid, Product>> LoadCatalogAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken,
        bool requireActive = false)
    {
        var ids = productIds.Distinct().ToList();

        IReadOnlyCollection<ProductDto> products = await _productQueries.GetByIdsAsync(ids, cancellationToken);

        var missing = ids.Except(products.Select(p => p.Id)).ToList();

        if (missing.Count > 0)
        {
            throw new NotFoundException($"Unknown product id(s): {string.Join(", ", missing)}.");
        }

        if (requireActive)
        {
            var discontinued = products.Where(p => !p.IsActive).Select(p => p.Name).ToList();

            if (discontinued.Count > 0)
            {
                throw new ValidationException(
                    $"Discontinued and cannot be added to a delivery: {string.Join(", ", discontinued)}.");
            }
        }

        return products.ToDictionary(p => p.Id, ToProduct);
    }

    private static Product ToProduct(ProductDto dto) => Product.FromDto(
        dto.Id,
        dto.SquareCatalogObjectId,
        dto.SquareItemId,
        dto.Name,
        dto.ItemName,
        dto.VariationName,
        dto.Sku,
        dto.Price,
        dto.IsActive,
        dto.LastSyncedAt,
        dto.CreatedAt,
        dto.UpdatedAt);
}
