using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A day's run as Routific planned it: the deliveries on it, in the order the driver works them.
/// </summary>
/// <remarks>
/// <para>
/// <b>This system does not build routes.</b> Staff send the day's orders to Routific, do the
/// route building there, and publish. Routific then reports the finished route back and we
/// materialise it here — so every route originates from <see cref="FromPublished"/>, never from
/// a constructor staff drive.
/// </para>
/// <para>
/// What we add on top is the loading workflow: <see cref="StopsInLoadingOrder"/> and the
/// per-stop tick that makes sure the van is packed correctly and nothing is left behind.
/// </para>
/// <para>
/// Routific republishes routes when a dispatcher changes one, so
/// <see cref="ApplyPublished"/> must be safe to run repeatedly over the same route.
/// </para>
/// </remarks>
public class Route
{
    private readonly List<RouteStop> _stops = [];

    public Guid Id { get; private set; }

    /// <summary>Routific's route identifier. The correlation key for republishes.</summary>
    public string ExternalRouteId { get; private set; } = string.Empty;

    public DateTime ScheduledDate { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public RouteStatus Status { get; private set; }

    /// <summary>Driver as named by Routific. Not a local user — drivers live in Routific in Phase 1.</summary>
    public string? DriverName { get; private set; }
    public string? DriverEmail { get; private set; }

    public int? WorkingTimeSeconds { get; private set; }
    public double? DistanceKilometers { get; private set; }

    /// <summary>When Routific published it.</summary>
    public DateTime PublishedAt { get; private set; }

    /// <summary>When we received it. Distinct from <see cref="PublishedAt"/> if a webhook was retried.</summary>
    public DateTime ReceivedAt { get; private set; }

    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Stops in the order the driver drives them.</summary>
    public IReadOnlyList<RouteStop> Stops =>
        _stops.OrderBy(s => s.SequenceOrder).ToList();

    /// <summary>
    /// Stops in the order they should go into the van — the reverse of the driving order, so the
    /// first delivery ends up nearest the doors.
    /// </summary>
    public IReadOnlyList<RouteStop> StopsInLoadingOrder =>
        _stops.OrderByDescending(s => s.SequenceOrder).ToList();

    public int StopCount => _stops.Count;

    public IReadOnlyCollection<RouteStop> UnloadedStops => _stops.Where(s => !s.IsLoaded).ToList();

    public bool IsFullyLoaded => _stops.Count > 0 && _stops.All(s => s.IsLoaded);

    public bool IsClosed => Status is RouteStatus.Completed or RouteStatus.Canceled;

    private Route() { }

    /// <summary>
    /// Materialises a route from a Routific publication.
    /// </summary>
    public static Route FromPublished(
        string externalRouteId,
        DateTime scheduledDate,
        string name,
        RouteStatus status,
        DateTime publishedAt,
        IReadOnlyCollection<PublishedRouteStop> stops,
        string? driverName = null,
        string? driverEmail = null,
        int? workingTimeSeconds = null,
        double? distanceKilometers = null)
    {
        if (string.IsNullOrWhiteSpace(externalRouteId))
            throw new DomainException("A published route must carry its external route identifier.");

        ArgumentNullException.ThrowIfNull(stops);

        var route = new Route
        {
            Id = Guid.NewGuid(),
            ExternalRouteId = externalRouteId.Trim(),
            ScheduledDate = scheduledDate.Date,
            Name = string.IsNullOrWhiteSpace(name) ? externalRouteId.Trim() : name.Trim(),
            Status = status,
            PublishedAt = publishedAt,
            ReceivedAt = DateTime.UtcNow,
            DriverName = driverName?.Trim(),
            DriverEmail = driverEmail?.Trim(),
            WorkingTimeSeconds = workingTimeSeconds,
            DistanceKilometers = distanceKilometers
        };

        route.ReplaceStops(stops);

        return route;
    }

    public static Route FromDto(
        Guid id,
        string externalRouteId,
        DateTime scheduledDate,
        string name,
        RouteStatus status,
        string? driverName,
        string? driverEmail,
        int? workingTimeSeconds,
        double? distanceKilometers,
        DateTime publishedAt,
        DateTime receivedAt,
        DateTime? updatedAt,
        IEnumerable<RouteStop>? stops = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid route ID.");

        var route = new Route
        {
            Id = id,
            ExternalRouteId = externalRouteId,
            ScheduledDate = scheduledDate,
            Name = name,
            Status = status,
            DriverName = driverName,
            DriverEmail = driverEmail,
            WorkingTimeSeconds = workingTimeSeconds,
            DistanceKilometers = distanceKilometers,
            PublishedAt = publishedAt,
            ReceivedAt = receivedAt,
            UpdatedAt = updatedAt
        };

        if (stops is not null)
        {
            route._stops.AddRange(stops);
        }

        return route;
    }

    /// <summary>
    /// Re-applies a republication from Routific. Stops that survive keep their identity and their
    /// loading tick; stops no longer on the route are dropped and new ones added.
    /// </summary>
    public void ApplyPublished(
        DateTime scheduledDate,
        string name,
        RouteStatus status,
        DateTime publishedAt,
        IReadOnlyCollection<PublishedRouteStop> stops,
        string? driverName = null,
        string? driverEmail = null,
        int? workingTimeSeconds = null,
        double? distanceKilometers = null)
    {
        ArgumentNullException.ThrowIfNull(stops);

        ScheduledDate = scheduledDate.Date;
        Name = string.IsNullOrWhiteSpace(name) ? Name : name.Trim();
        Status = status;
        PublishedAt = publishedAt;
        DriverName = driverName?.Trim() ?? DriverName;
        DriverEmail = driverEmail?.Trim() ?? DriverEmail;
        WorkingTimeSeconds = workingTimeSeconds ?? WorkingTimeSeconds;
        DistanceKilometers = distanceKilometers ?? DistanceKilometers;

        ReplaceStops(stops);
        Touch();
    }

    /// <summary>
    /// Applies a status change reported by Routific without touching the stops.
    /// </summary>
    public bool ApplyStatus(RouteStatus status)
    {
        if (Status == status) return false;

        Status = status;
        Touch();
        return true;
    }

    public bool Contains(Guid deliveryId) => _stops.Any(s => s.DeliveryId == deliveryId);

    // --- Loading -----------------------------------------------------------

    public void MarkStopLoaded(Guid routeStopId)
    {
        FindStop(routeStopId).MarkLoaded();
        Touch();
    }

    public void ClearStopLoaded(Guid routeStopId)
    {
        FindStop(routeStopId).ClearLoaded();
        Touch();
    }

    // --- Internals ---------------------------------------------------------

    /// <summary>
    /// Reconciles the stop list against a publication, preserving surviving stops so their
    /// loading state is not lost when a dispatcher republishes.
    /// </summary>
    private void ReplaceStops(IReadOnlyCollection<PublishedRouteStop> stops)
    {
        var incoming = stops
            .Where(s => s.DeliveryId != Guid.Empty)
            .GroupBy(s => s.DeliveryId)
            .Select(g => g.OrderBy(s => s.Sequence).First())
            .ToList();

        if (incoming.Count == 0)
            throw new DomainException("A published route must contain at least one delivery stop.");

        _stops.RemoveAll(existing => incoming.All(i => i.DeliveryId != existing.DeliveryId));

        foreach (var stop in incoming)
        {
            var data = new PublishedStopData(
                stop.DeliveryId,
                stop.Sequence,
                stop.ExternalStopId,
                stop.PlannedArrival,
                stop.PlannedDeparture,
                stop.ActualArrival,
                stop.ActualDeparture,
                stop.DistanceFromPreviousKm);

            var existing = _stops.FirstOrDefault(s => s.DeliveryId == stop.DeliveryId);

            if (existing is null)
            {
                _stops.Add(RouteStop.FromPublished(Id, data));
            }
            else
            {
                existing.ApplyPublished(data);
            }
        }
    }

    private RouteStop FindStop(Guid routeStopId) =>
        _stops.FirstOrDefault(s => s.Id == routeStopId)
            ?? throw new DomainException($"Route stop '{routeStopId}' was not found on this route.");

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
