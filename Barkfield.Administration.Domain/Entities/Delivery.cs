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
    public Guid SubscriptionId { get; private set; }
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

    /// <summary>The customer location this delivery goes to.</summary>
    public Guid DeliveryLocationId { get; private set; }

    /// <summary>
    /// Address as it was when the delivery was scheduled. Snapshotted for the same reason line
    /// items snapshot product name and price: a customer moving house must not rewrite where
    /// last month's delivery went.
    /// </summary>
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
    /// <param name="location">
    /// Where this delivery goes. Its address and coordinates are snapshotted onto the delivery.
    /// </param>
    public static Delivery Schedule(
        Guid subscriptionId,
        Guid customerId,
        DeliveryManifest manifest,
        FulfillmentMethod fulfillmentMethod,
        IReadOnlyDictionary<Guid, Product> catalog,
        DeliveryLocation location)
    {
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A delivery must belong to a valid subscription.");

        if (customerId == Guid.Empty)
            throw new DomainException("A delivery must belong to a valid customer.");

        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(location);

        if (location.CustomerId != customerId)
            throw new DomainException("The delivery location belongs to a different customer.");

        manifest.EnsureNotEmpty();

        var delivery = new Delivery
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            CustomerId = customerId,
            ScheduledFor = manifest.DeliveryDate.Date,
            Status = DeliveryStatus.Scheduled,
            FulfillmentMethod = fulfillmentMethod,
            ProcurementStatus = ProcurementStatus.NotStarted,
            DeliveryLocationId = location.Id,
            DeliveryAddress = location.Address,
            DeliveryCoordinates = location.Coordinates,
            RequestedWindow = location.PreferredWindow,
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

    public static Delivery FromDto(
        Guid id,
        Guid subscriptionId,
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
        Guid deliveryLocationId,
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
            DeliveryLocationId = deliveryLocationId,
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
