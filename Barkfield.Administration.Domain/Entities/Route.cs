using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A day's run for one van: the deliveries assigned to it, in the order the optimiser worked out.
/// </summary>
/// <remarks>
/// <para>
/// <b>Staff own the split, the optimiser owns the sequence.</b> Staff create however many routes
/// a day needs and assign deliveries between them; there is deliberately no method to hand-order
/// stops. <see cref="SequenceOrder"/> always comes from <see cref="ApplyOptimizedOrder"/>.
/// </para>
/// <para>
/// Because each route is solved independently, the optimisation problem is a single-vehicle
/// travelling-salesman with a fixed depot at both ends — not a fleet assignment problem.
/// </para>
/// <para>
/// Assigning or unassigning a delivery stamps <see cref="StopsChangedAt"/>, which makes
/// <see cref="NeedsOptimization"/> true and blocks publishing until the route is re-optimised.
/// </para>
/// <para>
/// A route cannot be published without a driver. Publishing is what releases it to the driver
/// app: the caller pushes a notification to the driver's devices and calls
/// <see cref="MarkDriverNotified"/>; the driver opening it calls <see cref="AcknowledgeByDriver"/>.
/// Drivers are ordinary <see cref="User"/>s holding the driver role, so they authenticate through
/// the existing identity system rather than a parallel one.
/// </para>
/// </remarks>
public class Route
{
    private readonly List<RouteStop> _stops = [];

    public Guid Id { get; private set; }

    /// <summary>The delivery day this route runs on.</summary>
    public DateTime ScheduledDate { get; private set; }

    /// <summary>Staff-facing label, e.g. "Route 1" or "North Shore".</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Where the run starts and ends.</summary>
    public Guid DepotId { get; private set; }

    /// <summary>
    /// The driver running this route. A staff <see cref="User"/> holding the driver role —
    /// drivers authenticate through the same identity system as everyone else.
    /// </summary>
    public Guid? AssignedDriverUserId { get; private set; }

    public RouteStatus Status { get; private set; }

    /// <summary>When the van is planned to leave the depot. Anchors the estimated arrival times.</summary>
    public TimeOnly PlannedStartTime { get; private set; }

    /// <summary>Optional end of the driver's shift, so the optimiser can avoid overrunning it.</summary>
    public TimeOnly? PlannedEndTime { get; private set; }

    /// <summary>Set once the driver's device has been told the route is ready.</summary>
    public DateTime? DriverNotifiedAt { get; private set; }

    /// <summary>Set when the driver opens the route in the app. Staff can see who has not picked up yet.</summary>
    public DateTime? DriverAcknowledgedAt { get; private set; }

    public DateTime? OptimizedAt { get; private set; }
    public DateTime? StopsChangedAt { get; private set; }

    /// <summary>Which travel-time data produced the current ordering.</summary>
    public RouteOptimizationSource? OptimizationSource { get; private set; }

    public int? EstimatedTravelSeconds { get; private set; }
    public int? EstimatedTotalSeconds { get; private set; }

    public DateTime? PublishedAt { get; private set; }
    public DateTime? StartedAt { get; private set; }
    public DateTime? CompletedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Stops in run order. Unordered until the route has been optimised.</summary>
    public IReadOnlyList<RouteStop> Stops =>
        _stops.OrderBy(s => s.SequenceOrder).ThenBy(s => s.CreatedAt).ToList();

    public int StopCount => _stops.Count;

    /// <summary>True when the stop list has changed since the last optimisation.</summary>
    public bool NeedsOptimization =>
        _stops.Count > 0 && (OptimizedAt is null || (StopsChangedAt is not null && StopsChangedAt > OptimizedAt));

    /// <summary>True when the ordering came from the straight-line fallback rather than real roads.</summary>
    public bool HasApproximateOrdering => OptimizationSource == RouteOptimizationSource.Approximate;

    public bool IsClosed => Status is RouteStatus.Completed or RouteStatus.Canceled;

    public bool HasDriver => AssignedDriverUserId is not null;

    /// <summary>Published to a driver who has not opened it yet. Worth surfacing before the van should leave.</summary>
    public bool IsAwaitingDriverAcknowledgement =>
        Status == RouteStatus.Published && DriverAcknowledgedAt is null;

    /// <summary>Stops still waiting to be ticked into the van.</summary>
    public IReadOnlyCollection<RouteStop> UnloadedStops => _stops.Where(s => !s.IsLoaded).ToList();

    public bool IsFullyLoaded => _stops.Count > 0 && _stops.All(s => s.IsLoaded);

    private Route() { }

    public static Route Create(
        DateTime scheduledDate,
        string name,
        Guid depotId,
        TimeOnly plannedStartTime,
        TimeOnly? plannedEndTime = null,
        Guid? assignedDriverUserId = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Route name is required.");

        if (depotId == Guid.Empty)
            throw new DomainException("A route must start and end at a valid depot.");

        if (plannedEndTime.HasValue && plannedEndTime.Value <= plannedStartTime)
            throw new DomainException("A route's shift must end after it starts.");

        return new Route
        {
            Id = Guid.NewGuid(),
            ScheduledDate = scheduledDate.Date,
            Name = name.Trim(),
            DepotId = depotId,
            PlannedStartTime = plannedStartTime,
            PlannedEndTime = plannedEndTime,
            AssignedDriverUserId = assignedDriverUserId,
            Status = RouteStatus.Draft,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Route FromDto(
        Guid id,
        DateTime scheduledDate,
        string name,
        Guid depotId,
        Guid? assignedDriverUserId,
        RouteStatus status,
        TimeOnly plannedStartTime,
        TimeOnly? plannedEndTime,
        DateTime? driverNotifiedAt,
        DateTime? driverAcknowledgedAt,
        DateTime? optimizedAt,
        DateTime? stopsChangedAt,
        RouteOptimizationSource? optimizationSource,
        int? estimatedTravelSeconds,
        int? estimatedTotalSeconds,
        DateTime? publishedAt,
        DateTime? startedAt,
        DateTime? completedAt,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<RouteStop>? stops = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid route ID.");

        var route = new Route
        {
            Id = id,
            ScheduledDate = scheduledDate,
            Name = name,
            DepotId = depotId,
            AssignedDriverUserId = assignedDriverUserId,
            Status = status,
            PlannedStartTime = plannedStartTime,
            PlannedEndTime = plannedEndTime,
            DriverNotifiedAt = driverNotifiedAt,
            DriverAcknowledgedAt = driverAcknowledgedAt,
            OptimizedAt = optimizedAt,
            StopsChangedAt = stopsChangedAt,
            OptimizationSource = optimizationSource,
            EstimatedTravelSeconds = estimatedTravelSeconds,
            EstimatedTotalSeconds = estimatedTotalSeconds,
            PublishedAt = publishedAt,
            StartedAt = startedAt,
            CompletedAt = completedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (stops is not null)
        {
            route._stops.AddRange(stops);
        }

        return route;
    }

    // --- Assignment --------------------------------------------------------

    /// <summary>
    /// Puts a delivery on this route. Order is not decided here — the route now needs optimising.
    /// </summary>
    public RouteStop AssignDelivery(Guid deliveryId)
    {
        EnsureEditable();

        if (deliveryId == Guid.Empty)
            throw new DomainException("A valid delivery id is required.");

        if (_stops.Any(s => s.DeliveryId == deliveryId))
            throw new DomainException("That delivery is already on this route.");

        var stop = RouteStop.Create(Id, deliveryId);
        _stops.Add(stop);

        MarkStopsChanged();

        return stop;
    }

    /// <summary>
    /// Takes a delivery off this route, returning it to the unassigned pool.
    /// </summary>
    public void UnassignDelivery(Guid deliveryId)
    {
        EnsureEditable();

        if (_stops.RemoveAll(s => s.DeliveryId == deliveryId) > 0)
        {
            MarkStopsChanged();
        }
    }

    public bool Contains(Guid deliveryId) => _stops.Any(s => s.DeliveryId == deliveryId);

    // --- Optimisation ------------------------------------------------------

    /// <summary>
    /// Applies an optimiser's answer: the run order and estimated arrival times.
    /// </summary>
    /// <param name="orderedStops">
    /// Every delivery currently on this route, exactly once, in run order.
    /// </param>
    /// <param name="source">
    /// Whether real road travel times or the straight-line fallback produced this ordering.
    /// </param>
    /// <remarks>
    /// Takes plain values, never vendor types, so the domain stays ignorant of which engine ran.
    /// </remarks>
    public void ApplyOptimizedOrder(
        IReadOnlyList<OptimizedStop> orderedStops,
        RouteOptimizationSource source,
        int? estimatedTravelSeconds = null,
        int? estimatedTotalSeconds = null)
    {
        EnsureEditable();
        ArgumentNullException.ThrowIfNull(orderedStops);

        if (_stops.Count == 0)
            throw new DomainException("There is nothing to optimise on an empty route.");

        var suppliedIds = orderedStops.Select(s => s.DeliveryId).ToList();

        if (suppliedIds.Count != _stops.Count
            || suppliedIds.Distinct().Count() != suppliedIds.Count
            || suppliedIds.Any(id => _stops.All(s => s.DeliveryId != id)))
        {
            throw new DomainException("An optimised order must list every delivery on this route exactly once.");
        }

        for (int i = 0; i < orderedStops.Count; i++)
        {
            OptimizedStop optimized = orderedStops[i];
            RouteStop stop = _stops.First(s => s.DeliveryId == optimized.DeliveryId);

            stop.ApplyOptimization(i + 1, optimized.EstimatedArrival, optimized.EstimatedDeparture);
        }

        OptimizedAt = DateTime.UtcNow;
        OptimizationSource = source;
        EstimatedTravelSeconds = estimatedTravelSeconds;
        EstimatedTotalSeconds = estimatedTotalSeconds;

        if (Status == RouteStatus.Draft)
        {
            Status = RouteStatus.Optimized;
        }

        Touch();
    }

    // --- Loading -----------------------------------------------------------

    public void MarkStopLoaded(Guid routeStopId)
    {
        EnsureOpen();
        FindStop(routeStopId).MarkLoaded();
        Touch();
    }

    public void ClearStopLoaded(Guid routeStopId)
    {
        EnsureOpen();
        FindStop(routeStopId).ClearLoaded();
        Touch();
    }

    /// <summary>
    /// Records the driver arriving at a stop. Idempotent, because the driver app replays queued
    /// actions after losing signal.
    /// </summary>
    public void RecordStopArrival(Guid routeStopId, DateTime arrivedAt)
    {
        FindStop(routeStopId).RecordArrival(arrivedAt);
        Touch();
    }

    // --- Lifecycle ---------------------------------------------------------

    /// <summary>
    /// Releases the route to the driver app. The caller is expected to push a notification to the
    /// assigned driver's devices and then call <see cref="MarkDriverNotified"/>.
    /// </summary>
    public void Publish()
    {
        EnsureEditable();

        if (_stops.Count == 0)
            throw new DomainException("An empty route cannot be published.");

        if (!HasDriver)
            throw new DomainException("A route cannot be published without an assigned driver.");

        if (NeedsOptimization)
            throw new DomainException("This route has changed since it was last optimised. Re-optimise before publishing.");

        Status = RouteStatus.Published;
        PublishedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Records that the push notification went out to the driver's devices.
    /// </summary>
    public void MarkDriverNotified()
    {
        if (Status != RouteStatus.Published)
            throw new DomainException("Only a published route can be sent to a driver.");

        DriverNotifiedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Records the driver opening the route in the app.
    /// </summary>
    /// <returns>True if this changed anything; false if already acknowledged.</returns>
    /// <remarks>
    /// Idempotent — the driver app replays queued actions after signal loss, so a repeat must be
    /// a no-op rather than an error.
    /// </remarks>
    public bool AcknowledgeByDriver()
    {
        if (Status != RouteStatus.Published) return false;
        if (DriverAcknowledgedAt is not null) return false;

        DriverAcknowledgedAt = DateTime.UtcNow;
        Touch();
        return true;
    }

    public void Start()
    {
        if (Status != RouteStatus.Published)
            throw new DomainException($"A route in '{Status}' cannot be started; it must be published first.");

        Status = RouteStatus.InProgress;
        StartedAt = DateTime.UtcNow;
        Touch();
    }

    public void Complete()
    {
        if (Status is not (RouteStatus.InProgress or RouteStatus.Published))
            throw new DomainException($"A route in '{Status}' cannot be completed.");

        Status = RouteStatus.Completed;
        CompletedAt = DateTime.UtcNow;
        Touch();
    }

    public void Cancel()
    {
        if (Status == RouteStatus.Completed)
            throw new DomainException("A completed route cannot be canceled.");

        Status = RouteStatus.Canceled;
        Touch();
    }

    // --- Details -----------------------------------------------------------

    public void Rename(string name)
    {
        EnsureOpen();

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Route name is required.");

        Name = name.Trim();
        Touch();
    }

    /// <summary>
    /// Assigns the driver who will run this route. Reassigning a published route resets the
    /// notification state, because the new driver has not been told about it.
    /// </summary>
    public void AssignDriver(Guid? userId)
    {
        EnsureOpen();

        if (AssignedDriverUserId == userId) return;

        AssignedDriverUserId = userId;
        DriverNotifiedAt = null;
        DriverAcknowledgedAt = null;
        Touch();
    }

    /// <summary>
    /// Changes the shift window the optimiser should plan within.
    /// </summary>
    public void ChangeShiftEnd(TimeOnly? plannedEndTime)
    {
        EnsureEditable();

        if (plannedEndTime.HasValue && plannedEndTime.Value <= PlannedStartTime)
            throw new DomainException("A route's shift must end after it starts.");

        PlannedEndTime = plannedEndTime;
        MarkStopsChanged();
    }

    /// <summary>
    /// Changes the planned departure. Estimated arrival times are anchored to it, so the route
    /// needs re-optimising.
    /// </summary>
    public void ChangeStartTime(TimeOnly plannedStartTime)
    {
        EnsureEditable();

        PlannedStartTime = plannedStartTime;
        MarkStopsChanged();
    }

    // --- Internals ---------------------------------------------------------

    private RouteStop FindStop(Guid routeStopId) =>
        _stops.FirstOrDefault(s => s.Id == routeStopId)
            ?? throw new DomainException($"Route stop '{routeStopId}' was not found on this route.");

    private void MarkStopsChanged()
    {
        StopsChangedAt = DateTime.UtcNow;

        // A published route whose contents changed is no longer what the driver was given, so it
        // returns to the staff's hands and the driver has to be notified again once re-published.
        if (Status == RouteStatus.Published)
        {
            Status = RouteStatus.Optimized;
            PublishedAt = null;
            DriverNotifiedAt = null;
            DriverAcknowledgedAt = null;
        }

        Touch();
    }

    private void EnsureEditable()
    {
        if (Status is RouteStatus.InProgress)
            throw new DomainException("A route that is already running cannot be changed.");

        EnsureOpen();
    }

    private void EnsureOpen()
    {
        if (IsClosed)
            throw new DomainException($"This route is already '{Status}' and cannot be changed.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
