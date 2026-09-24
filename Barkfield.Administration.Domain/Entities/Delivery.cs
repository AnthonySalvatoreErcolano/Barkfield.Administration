using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// One dispatch of a subscription: what was in the box, where it was in the workflow, whether
/// the stock was procured, and whether it was paid for. The Phase 1 delivery log.
/// </summary>
/// <remarks>
/// <para>
/// A separate aggregate from <see cref="Subscription"/>, because deliveries accumulate without
/// bound and must not be loaded alongside the subscription they belong to.
/// </para>
/// <para>
/// Workflow state (<see cref="Status"/>), procurement (<see cref="ProcurementStatus"/>), payment
/// and loyalty live here rather than on the subscription, so each dispatch keeps its own history
/// instead of overwriting a single shared field.
/// </para>
/// <para>
/// Two families of state-changing methods:
/// <c>Mark*</c> / <c>Substitute*</c> are staff actions and enforce the workflow strictly.
/// <c>Record*</c> are for ingesting Routific webhooks: idempotent, forward-only, and they never
/// throw on a repeated or out-of-order event.
/// </para>
/// </remarks>
public class Delivery
{
    private readonly List<DeliveryLine> _lines = [];

    public Guid Id { get; private set; }

    /// <summary>
    /// The subscription this dispatch came from, or null for a one-off.
    /// </summary>
    /// <remarks>
    /// A customer already in the system can ask for something on a day they are not scheduled
    /// without touching their subscription. That is a real delivery — contents, procurement,
    /// billing, history — it simply has no recurring order behind it.
    /// </remarks>
    public Guid? SubscriptionId { get; private set; }

    public Guid CustomerId { get; private set; }

    public DateTime ScheduledFor { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public DeliveryStatus Status { get; private set; }
    public FulfillmentMethod FulfillmentMethod { get; private set; }

    /// <summary>
    /// Rollup of every line's procurement state. Recalculated on each line change; never set directly.
    /// </summary>
    public ProcurementStatus ProcurementStatus { get; private set; }

    public bool HasPaid { get; private set; }

    /// <summary>Astro Loyalty rewards applied. Phase 2 automates this; staff tick it until then.</summary>
    public bool AstroCompleted { get; private set; }

    /// <summary>
    /// The routing provider's identifier for this delivery, once pushed. Our own
    /// <see cref="Id"/> is sent as their customer order number, so it round-trips on every
    /// webhook — this is kept for direct calls back to them (fetching photos, cancelling).
    /// </summary>
    public string? ExternalOrderId { get; private set; }

    /// <summary>Set when this delivery was pushed to the routing provider for planning.</summary>
    public DateTime? SentToRoutingAt { get; private set; }

    /// <summary>
    /// Address as it was when the delivery was scheduled. Snapshotted for the same reason line
    /// items snapshot product name and price: a customer moving house must not rewrite where
    /// last month's delivery went.
    /// </summary>
    /// <remarks>
    /// This snapshot is the whole mechanism protecting delivery history. There is deliberately
    /// no reference to a separate location record — a delivery always goes to the customer's
    /// own address, and holding a second copy of it elsewhere would only create two versions
    /// of the truth to keep in step.
    /// </remarks>
    public Address DeliveryAddress { get; private set; } = null!;

    /// <summary>Coordinates as snapshotted at scheduling time. What the optimiser routes to.</summary>
    public GeoPoint? DeliveryCoordinates { get; private set; }

    /// <summary>Per-delivery override of the location's preferred window.</summary>
    public TimeWindow? RequestedWindow { get; private set; }

    /// <summary>Per-delivery override of the location's default stop duration, in minutes.</summary>
    public int? ServiceDurationMinutesOverride { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Set when a delivery is attempted but does not complete.</summary>
    public string? FailureReason { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<DeliveryLine> Lines => _lines.AsReadOnly();

    /// <summary>True when this dispatch is not tied to a recurring order.</summary>
    public bool IsOneOff => SubscriptionId is null;

    /// <summary>
    /// True once the contents are fixed, because the customer has been charged for them.
    /// </summary>
    /// <remarks>
    /// Adding, removing, repricing or substituting a line after payment would mean the customer
    /// paid for a different box from the one they receive. Recording what actually happened —
    /// received, out of stock, shorted — stays open, because a paid delivery that came up short
    /// is a refund to arrange, not something to hide from the record.
    /// </remarks>
    public bool ContentsAreLocked => HasPaid;

    /// <summary>Order value, accounting for substitutions and shorted lines.</summary>
    public decimal Total => _lines.Sum(l => l.LineTotal);

    public int TotalUnits => _lines.Sum(l => l.Quantity);

    public bool IsClosed => Status is DeliveryStatus.Delivered or DeliveryStatus.Failed or DeliveryStatus.Canceled;

    /// <summary>True when every line is resolved and the box can be packed.</summary>
    public bool IsReadyToPack => ProcurementStatus == ProcurementStatus.Ready;

    /// <summary>Lines still needing a procurement decision, for the staff worklist.</summary>
    public IReadOnlyCollection<DeliveryLine> UnresolvedLines =>
        _lines.Where(l => !l.IsResolved).ToList();

    private Delivery() { }

    /// <summary>
    /// Creates a delivery from a subscription's resolved manifest, snapshotting each product's
    /// current name and price. All lines start <see cref="LineOrderStatus.Pending"/>.
    /// </summary>
    /// <param name="catalog">
    /// The products referenced by the manifest, keyed by product id. The caller resolves these;
    /// the domain does not read from storage.
    /// </param>
    /// <param name="customer">
    /// Who this is for. Their address, coordinates and preferred window are snapshotted onto
    /// the delivery — a delivery always goes to the customer's own address.
    /// </param>
    public static Delivery Schedule(
        Guid subscriptionId,
        DeliveryManifest manifest,
        FulfillmentMethod fulfillmentMethod,
        IReadOnlyDictionary<Guid, Product> catalog,
        Customer customer)
    {
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A delivery must belong to a valid subscription.");

        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(customer);

        // Pickup and shipping legitimately need no address; a driven route cannot happen
        // without one, and finding that out on the van is too late.
        if (fulfillmentMethod == FulfillmentMethod.LocalDelivery && !customer.CanReceiveLocalDelivery)
            throw new DomainException($"{customer.FullName} has no address on file, so a local delivery cannot be scheduled.");

        manifest.EnsureNotEmpty();

        var delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            CustomerId = customer.Id,
            ScheduledFor = manifest.DeliveryDate.Date,
            Status = DeliveryStatus.Scheduled,
            FulfillmentMethod = fulfillmentMethod,
            ProcurementStatus = ProcurementStatus.NotStarted,
            DeliveryAddress = customer.Address ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty),
            DeliveryCoordinates = customer.Coordinates,
            RequestedWindow = customer.PreferredWindow,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var line in manifest.Lines)
        {
            if (!catalog.TryGetValue(line.ProductId, out var product))
                throw new DomainException($"Product '{line.ProductId}' is on the manifest but was not supplied in the catalog.");

            delivery._lines.Add(DeliveryLine.Create(
                delivery.Id,
                product.Id,
                product.Name,
                product.Price,
                line.Quantity,
                line.Source,
                line.SourceId));
        }

        return delivery;
    }

    /// <summary>
    /// Creates a delivery that belongs to no subscription — a one-off for a customer already in
    /// the system, on a day they are not otherwise scheduled.
    /// </summary>
    /// <remarks>
    /// Its lines are <see cref="DeliveryLineSource.Manual"/>: nothing recurring put them there,
    /// so there is no source row for them to point back at.
    /// </remarks>
    /// <param name="requestedLines">Product id and quantity per line. Must not be empty.</param>
    public static Delivery ScheduleOneOff(
        Customer customer,
        DateTime scheduledFor,
        FulfillmentMethod fulfillmentMethod,
        IReadOnlyCollection<(Guid ProductId, int Quantity)> requestedLines,
        IReadOnlyDictionary<Guid, Product> catalog)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(requestedLines);
        ArgumentNullException.ThrowIfNull(catalog);

        if (requestedLines.Count == 0)
            throw new DomainException("A delivery cannot be scheduled with no line items.");

        if (fulfillmentMethod == FulfillmentMethod.LocalDelivery && !customer.CanReceiveLocalDelivery)
            throw new DomainException($"{customer.FullName} has no address on file, so a local delivery cannot be scheduled.");

        var delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            SubscriptionId = null,
            CustomerId = customer.Id,
            ScheduledFor = scheduledFor.Date,
            Status = DeliveryStatus.Scheduled,
            FulfillmentMethod = fulfillmentMethod,
            ProcurementStatus = ProcurementStatus.NotStarted,
            DeliveryAddress = customer.Address ?? new Address(string.Empty, string.Empty, string.Empty, string.Empty),
            DeliveryCoordinates = customer.Coordinates,
            RequestedWindow = customer.PreferredWindow,
            CreatedAt = DateTime.UtcNow
        };

        foreach ((Guid productId, int quantity) in requestedLines)
        {
            if (!catalog.TryGetValue(productId, out Product? product))
                throw new DomainException($"Product '{productId}' was requested but was not supplied in the catalog.");

            delivery._lines.Add(DeliveryLine.Create(
                delivery.Id, product.Id, product.Name, product.Price, quantity,
                DeliveryLineSource.Manual, sourceId: null));
        }

        return delivery;
    }

    public static Delivery FromDto(
        Guid id,
        Guid? subscriptionId,
        Guid customerId,
        DateTime scheduledFor,
        DateTime? completedAt,
        DeliveryStatus status,
        FulfillmentMethod fulfillmentMethod,
        ProcurementStatus procurementStatus,
        bool hasPaid,
        bool astroCompleted,
        string? externalOrderId,
        DateTime? sentToRoutingAt,
        Address deliveryAddress,
        GeoPoint? deliveryCoordinates,
        TimeWindow? requestedWindow,
        int? serviceDurationMinutesOverride,
        string? notes,
        string? failureReason,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<DeliveryLine>? lines = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid delivery ID.");

        var delivery = new Delivery
        {
            Id = id,
            SubscriptionId = subscriptionId,
            CustomerId = customerId,
            ScheduledFor = scheduledFor,
            CompletedAt = completedAt,
            Status = status,
            FulfillmentMethod = fulfillmentMethod,
            ProcurementStatus = procurementStatus,
            HasPaid = hasPaid,
            AstroCompleted = astroCompleted,
            ExternalOrderId = externalOrderId,
            SentToRoutingAt = sentToRoutingAt,
            DeliveryAddress = deliveryAddress,
            DeliveryCoordinates = deliveryCoordinates,
            RequestedWindow = requestedWindow,
            ServiceDurationMinutesOverride = serviceDurationMinutesOverride,
            Notes = notes,
            FailureReason = failureReason,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (lines is not null)
        {
            delivery._lines.AddRange(lines);
        }

        return delivery;
    }

    // --- Contents (editable until the customer has been charged) -----------

    /// <summary>
    /// Adds a product to this delivery by hand.
    /// </summary>
    /// <remarks>
    /// The "she called and wants a bag added" case. Adding a product already on the delivery
    /// raises that line instead of creating a second one for the same thing.
    /// </remarks>
    public DeliveryLine AddLine(Product product, int quantity)
    {
        ArgumentNullException.ThrowIfNull(product);

        EnsureContentsEditable();

        if (!product.IsActive)
            throw new DomainException($"'{product.Name}' has been discontinued and cannot be added to a delivery.");

        DeliveryLine? existing = _lines.FirstOrDefault(l => l.ProductId == product.Id);

        if (existing is not null)
        {
            existing.ChangeQuantity(existing.Quantity + quantity);

            RecalculateProcurementStatus();
            Touch();

            return existing;
        }

        var line = DeliveryLine.Create(
            Id, product.Id, product.Name, product.Price, quantity,
            DeliveryLineSource.Manual, sourceId: null);

        _lines.Add(line);

        RecalculateProcurementStatus();
        Touch();

        return line;
    }

    /// <summary>
    /// Changes how much of a product is going out.
    /// </summary>
    /// <remarks>
    /// On a line that came from a subscription this makes the delivery diverge from it, which is
    /// intended — a delivery is a snapshot of what ships on one day, not a live view of the
    /// standing order.
    /// </remarks>
    public void ChangeLineQuantity(Guid deliveryLineId, int quantity)
    {
        EnsureContentsEditable();

        DeliveryLine line = FindLine(deliveryLineId);

        line.ChangeQuantity(quantity);

        RecalculateProcurementStatus();
        Touch();
    }

    /// <summary>
    /// Takes a product off this delivery entirely.
    /// </summary>
    /// <remarks>
    /// Different from <see cref="ShortLine"/>: shorting says "we meant to send this and could
    /// not", and stays on the record. Removing says it was never meant to go.
    /// </remarks>
    public void RemoveLine(Guid deliveryLineId)
    {
        EnsureContentsEditable();

        DeliveryLine line = FindLine(deliveryLineId);

        if (_lines.Count == 1)
            throw new DomainException("A delivery must have at least one line. Cancel it instead of emptying it.");

        _lines.Remove(line);

        RecalculateProcurementStatus();
        Touch();
    }

    // --- Procurement (manual staff actions) --------------------------------

    /// <summary>Marks a line as on order with the supplier.</summary>
    public void MarkLineOrdered(Guid deliveryLineId, string? note = null)
        => MutateLine(deliveryLineId, line => line.MarkOrdered(note));

    /// <summary>
    /// Marks a line fully received and set aside. Staff use this directly for shelf stock,
    /// without an intervening Ordered step.
    /// </summary>
    public void MarkLineReceived(Guid deliveryLineId, string? note = null)
        => MutateLine(deliveryLineId, line => line.Receive(line.Quantity, note));

    /// <summary>
    /// Records a partial receipt. The status follows the count: a full quantity resolves the
    /// line, anything less leaves it partially received.
    /// </summary>
    public void ReceiveLineQuantity(Guid deliveryLineId, int quantityReceived, string? note = null)
        => MutateLine(deliveryLineId, line => line.Receive(quantityReceived, note));

    /// <summary>Flags a line as unfillable. Blocks packing until substituted or shorted.</summary>
    public void MarkLineOutOfStock(Guid deliveryLineId, string? note = null)
        => MutateLine(deliveryLineId, line => line.MarkOutOfStock(note));

    /// <summary>
    /// Swaps a line for a different product, keeping the original on the record so the delivery
    /// still shows what was meant to ship.
    /// </summary>
    public void SubstituteLine(Guid deliveryLineId, Product substitute, string? note = null)
    {
        ArgumentNullException.ThrowIfNull(substitute);

        // A substitute carries its own price, so this changes what the delivery is worth —
        // which makes it a change to the contents, not just a record of them.
        EnsureContentsEditable();

        MutateLine(deliveryLineId, line =>
            line.Substitute(substitute.Id, substitute.Name, substitute.Price, note));
    }

    /// <summary>Ships without a line, knowingly. Resolves an out-of-stock.</summary>
    public void ShortLine(Guid deliveryLineId, string? note = null)
        => MutateLine(deliveryLineId, line => line.Short(note));

    /// <summary>Returns a line to untouched, for correcting a mis-click.</summary>
    public void ResetLineProcurement(Guid deliveryLineId, string? note = null)
        => MutateLine(deliveryLineId, line => line.ResetProcurement(note));

    /// <summary>
    /// Marks every unresolved line as on order in one action — placing a PO for a whole delivery
    /// is a single act. There is deliberately no bulk receive: receiving is per-line so the
    /// status always reflects stock physically set aside.
    /// </summary>
    public void MarkAllLinesOrdered(string? note = null)
    {
        EnsureOpen();

        foreach (var line in _lines.Where(l => !l.IsResolved))
        {
            line.MarkOrdered(note);
        }

        RecalculateProcurementStatus();
        Touch();
    }

    // --- Workflow (staff actions, strict) ----------------------------------

    public void MarkPacked()
    {
        EnsureOpen();

        if (Status != DeliveryStatus.Scheduled)
            throw new DomainException($"A delivery in '{Status}' cannot be marked packed.");

        if (!IsReadyToPack)
        {
            string blockers = string.Join(", ", UnresolvedLines.Select(l => $"{l.ProductName} ({l.OrderStatus})"));
            throw new DomainException($"This delivery cannot be packed until every line is resolved. Outstanding: {blockers}.");
        }

        Status = DeliveryStatus.Packed;
        Touch();
    }

    public void MarkOutForDelivery()
    {
        EnsureOpen();

        if (Status != DeliveryStatus.Packed)
            throw new DomainException($"A delivery in '{Status}' cannot go out for delivery; it must be packed first.");

        Status = DeliveryStatus.OutForDelivery;
        Touch();
    }

    /// <summary>
    /// Records a successful delivery. The caller must also call
    /// <see cref="Subscription.CompleteDelivery"/> so the rotation advances and add-ons expire.
    /// </summary>
    public void MarkDelivered(DateTime deliveredOn)
    {
        EnsureOpen();

        Status = DeliveryStatus.Delivered;
        CompletedAt = deliveredOn;
        Touch();
    }

    public void MarkFailed(string reason)
    {
        EnsureOpen();

        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A failure reason is required.");

        Status = DeliveryStatus.Failed;
        FailureReason = reason.Trim();
        Touch();
    }

    public void Cancel()
    {
        EnsureOpen();

        Status = DeliveryStatus.Canceled;
        Touch();
    }

    // --- Workflow (Routific webhooks, idempotent) --------------------------

    /// <summary>
    /// Marks the delivery as out on the road. Called when the driver starts the run.
    /// </summary>
    /// <returns>True if this changed anything; false if it was a duplicate or stale action.</returns>
    /// <remarks>
    /// Tolerant by design: the driver app queues actions while out of signal and replays them,
    /// so repeats and out-of-order arrivals must be no-ops rather than errors.
    /// </remarks>
    public bool RecordDispatched()
    {
        if (Rank(Status) >= Rank(DeliveryStatus.OutForDelivery)) return false;

        Status = DeliveryStatus.OutForDelivery;
        Touch();
        return true;
    }

    /// <summary>
    /// Records a completed delivery from the driver app.
    /// </summary>
    /// <returns>True if this changed anything; false if it was a duplicate or the delivery was canceled.</returns>
    public bool RecordDelivered(DateTime deliveredOn)
    {
        if (Status is DeliveryStatus.Delivered or DeliveryStatus.Canceled) return false;

        Status = DeliveryStatus.Delivered;
        CompletedAt = deliveredOn;
        FailureReason = null;
        Touch();
        return true;
    }

    /// <summary>
    /// Applies a "stop failed" event from Routific.
    /// </summary>
    /// <returns>True if this changed anything; false if it was a duplicate or already completed.</returns>
    public bool RecordFailed(string reason)
    {
        if (Status is DeliveryStatus.Failed or DeliveryStatus.Delivered or DeliveryStatus.Canceled) return false;

        Status = DeliveryStatus.Failed;
        FailureReason = string.IsNullOrWhiteSpace(reason) ? "Reported failed by Routific." : reason.Trim();
        Touch();
        return true;
    }

    // --- Routing & flags ---------------------------------------------------

    /// <summary>
    /// Records that this delivery has been pushed to the routing provider for planning.
    /// </summary>
    /// <returns>True if this changed anything; false if it had already been sent.</returns>
    /// <remarks>
    /// Idempotent, because a staff double-click or a retried push must not create a second order.
    /// </remarks>
    public bool MarkSentToRouting(string externalOrderId)
    {
        if (string.IsNullOrWhiteSpace(externalOrderId))
            throw new DomainException("An external order identifier is required.");

        EnsureOpen();

        if (ExternalOrderId == externalOrderId.Trim()) return false;

        ExternalOrderId = externalOrderId.Trim();
        SentToRoutingAt = DateTime.UtcNow;
        Touch();
        return true;
    }

    /// <summary>
    /// Clears the routing provider link, when a delivery is pulled back before it ships.
    /// </summary>
    public void ClearRoutingLink()
    {
        EnsureOpen();

        ExternalOrderId = null;
        SentToRoutingAt = null;
        Touch();
    }

    /// <summary>
    /// Marks the delivery as sitting on a published route.
    /// </summary>
    /// <remarks>
    /// The route/stop link itself lives on <see cref="RouteStop"/>, which is the single source of
    /// truth for which route a delivery is on. This only mirrors the workflow state.
    /// </remarks>
    public void MarkRouted()
    {
        EnsureOpen();

        if (Status is not (DeliveryStatus.Packed or DeliveryStatus.Routed))
            throw new DomainException($"A delivery in '{Status}' cannot be routed; it must be packed first.");

        Status = DeliveryStatus.Routed;
        Touch();
    }

    /// <summary>
    /// Returns a routed delivery to the packed pool, when it is pulled off a route.
    /// </summary>
    public void ClearRouted()
    {
        EnsureOpen();

        if (Status != DeliveryStatus.Routed) return;

        Status = DeliveryStatus.Packed;
        Touch();
    }

    /// <summary>Overrides the location's preferred delivery window for this delivery only.</summary>
    public void SetRequestedWindow(TimeWindow? window)
    {
        EnsureOpen();

        RequestedWindow = window;
        Touch();
    }

    /// <summary>Overrides the location's default stop duration for this delivery only.</summary>
    public void SetServiceDurationOverride(int? minutes)
    {
        if (minutes is < 0 or > 480)
            throw new DomainException("Service duration must be between 0 and 480 minutes.");

        EnsureOpen();

        ServiceDurationMinutesOverride = minutes;
        Touch();
    }

    public void MarkPaid()
    {
        HasPaid = true;
        Touch();
    }

    public void MarkAstroCompleted()
    {
        AstroCompleted = true;
        Touch();
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        Touch();
    }

    // --- Internals ---------------------------------------------------------

    /// <summary>
    /// Guards a change to what is in the box, as opposed to a record of what happened to it.
    /// </summary>
    private void EnsureContentsEditable()
    {
        EnsureOpen();

        if (ContentsAreLocked)
        {
            throw new DomainException(
                "This delivery has already been charged, so its contents cannot be changed. "
                + "Refund or adjust the payment in Square first.");
        }
    }

    private DeliveryLine FindLine(Guid deliveryLineId) =>
        _lines.FirstOrDefault(l => l.Id == deliveryLineId)
            ?? throw new DomainException($"Delivery line '{deliveryLineId}' was not found on this delivery.");

    private void MutateLine(Guid deliveryLineId, Action<DeliveryLine> mutate)
    {
        EnsureOpen();

        var line = _lines.FirstOrDefault(l => l.Id == deliveryLineId)
            ?? throw new DomainException($"Delivery line '{deliveryLineId}' was not found on this delivery.");

        mutate(line);

        RecalculateProcurementStatus();
        Touch();
    }

    /// <summary>
    /// Derives the delivery-wide rollup from the lines. Called after every line change, so the
    /// persisted value cannot drift from what it summarises.
    /// </summary>
    private void RecalculateProcurementStatus()
    {
        if (_lines.Count == 0)
        {
            ProcurementStatus = ProcurementStatus.NotStarted;
            return;
        }

        if (_lines.Any(l => l.IsBlocking))
        {
            ProcurementStatus = ProcurementStatus.Blocked;
            return;
        }

        if (_lines.All(l => l.IsResolved))
        {
            ProcurementStatus = ProcurementStatus.Ready;
            return;
        }

        ProcurementStatus = _lines.All(l => l.OrderStatus == LineOrderStatus.Pending)
            ? ProcurementStatus.NotStarted
            : ProcurementStatus.InProgress;
    }

    private static int Rank(DeliveryStatus status) => status switch
    {
        DeliveryStatus.Scheduled => 0,
        DeliveryStatus.Packed => 1,
        DeliveryStatus.Routed => 2,
        DeliveryStatus.OutForDelivery => 3,
        _ => 4
    };

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainException($"This delivery is already '{Status}' and cannot be changed.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
