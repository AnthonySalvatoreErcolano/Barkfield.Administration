using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// One delivery's place on a route, with its loading and arrival telemetry.
/// </summary>
/// <remarks>
/// <para>
/// Carries no delivery outcome. Whether a delivery succeeded or failed lives on
/// <see cref="Delivery"/>, so the two can never disagree — the same rule that keeps
/// procurement status off <see cref="SubscriptionItem"/>.
/// </para>
/// <para>
/// <see cref="SequenceOrder"/> is always assigned by the optimiser. Staff choose which route a
/// delivery is on; they never hand-order the stops.
/// </para>
/// </remarks>
public class RouteStop
{
    public Guid Id { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid DeliveryId { get; private set; }

    /// <summary>1-based position in the run. Zero until the route has been optimised.</summary>
    public int SequenceOrder { get; private set; }

    public TimeOnly? EstimatedArrival { get; private set; }
    public TimeOnly? EstimatedDeparture { get; private set; }

    /// <summary>Set when staff tick the box into the van.</summary>
    public DateTime? LoadedAt { get; private set; }

    /// <summary>Set by the driver app on arrival.</summary>
    public DateTime? ArrivedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public bool IsLoaded => LoadedAt is not null;

    private RouteStop() { }

    internal static RouteStop Create(Guid routeId, Guid deliveryId)
    {
        if (routeId == Guid.Empty)
            throw new DomainException("A route stop must belong to a valid route.");

        if (deliveryId == Guid.Empty)
            throw new DomainException("A route stop must reference a valid delivery.");

        return new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            DeliveryId = deliveryId,
            SequenceOrder = 0,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static RouteStop FromDto(
        Guid id,
        Guid routeId,
        Guid deliveryId,
        int sequenceOrder,
        TimeOnly? estimatedArrival,
        TimeOnly? estimatedDeparture,
        DateTime? loadedAt,
        DateTime? arrivedAt,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid route stop ID.");

        return new RouteStop
        {
            Id = id,
            RouteId = routeId,
            DeliveryId = deliveryId,
            SequenceOrder = sequenceOrder,
            EstimatedArrival = estimatedArrival,
            EstimatedDeparture = estimatedDeparture,
            LoadedAt = loadedAt,
            ArrivedAt = arrivedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    internal void ApplyOptimization(int sequenceOrder, TimeOnly? estimatedArrival, TimeOnly? estimatedDeparture)
    {
        SequenceOrder = sequenceOrder;
        EstimatedArrival = estimatedArrival;
        EstimatedDeparture = estimatedDeparture;
        Touch();
    }

    internal void MarkLoaded()
    {
        LoadedAt ??= DateTime.UtcNow;
        Touch();
    }

    internal void ClearLoaded()
    {
        LoadedAt = null;
        Touch();
    }

    /// <summary>
    /// Records arrival. Idempotent — the driver app replays queued actions after signal loss.
    /// </summary>
    internal void RecordArrival(DateTime arrivedAt)
    {
        ArrivedAt ??= arrivedAt;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
