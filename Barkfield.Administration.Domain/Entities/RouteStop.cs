using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// One delivery's place on a route, as Routific ordered it, plus our own loading tick.
/// </summary>
/// <remarks>
/// Carries no delivery outcome. Whether a delivery succeeded or failed lives on
/// <see cref="Delivery"/>, so the two can never disagree — the same rule that keeps
/// procurement status off <see cref="SubscriptionItem"/>.
/// </remarks>
public class RouteStop
{
    public Guid Id { get; private set; }
    public Guid RouteId { get; private set; }
    public Guid DeliveryId { get; private set; }

    /// <summary>Position in the run, as sequenced by Routific.</summary>
    public int SequenceOrder { get; private set; }

    /// <summary>Routific's stop identifier, for correlating later updates.</summary>
    public string? ExternalStopId { get; private set; }

    public TimeOnly? PlannedArrival { get; private set; }
    public TimeOnly? PlannedDeparture { get; private set; }
    public TimeOnly? ActualArrival { get; private set; }
    public TimeOnly? ActualDeparture { get; private set; }

    public double? DistanceFromPreviousKm { get; private set; }

    /// <summary>Set when staff tick this delivery into the van.</summary>
    public DateTime? LoadedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public bool IsLoaded => LoadedAt is not null;

    private RouteStop() { }

    internal static RouteStop FromPublished(Guid routeId, PublishedStopData data)
    {
        if (routeId == Guid.Empty)
            throw new DomainException("A route stop must belong to a valid route.");

        if (data.DeliveryId == Guid.Empty)
            throw new DomainException("A route stop must reference a valid delivery.");

        return new RouteStop
        {
            Id = Guid.NewGuid(),
            RouteId = routeId,
            DeliveryId = data.DeliveryId,
            SequenceOrder = data.Sequence,
            ExternalStopId = data.ExternalStopId,
            PlannedArrival = data.PlannedArrival,
            PlannedDeparture = data.PlannedDeparture,
            ActualArrival = data.ActualArrival,
            ActualDeparture = data.ActualDeparture,
            DistanceFromPreviousKm = data.DistanceFromPreviousKm,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static RouteStop FromDto(
        Guid id,
        Guid routeId,
        Guid deliveryId,
        int sequenceOrder,
        string? externalStopId,
        TimeOnly? plannedArrival,
        TimeOnly? plannedDeparture,
        TimeOnly? actualArrival,
        TimeOnly? actualDeparture,
        double? distanceFromPreviousKm,
        DateTime? loadedAt,
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
            ExternalStopId = externalStopId,
            PlannedArrival = plannedArrival,
            PlannedDeparture = plannedDeparture,
            ActualArrival = actualArrival,
            ActualDeparture = actualDeparture,
            DistanceFromPreviousKm = distanceFromPreviousKm,
            LoadedAt = loadedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// Re-applies this stop's details when Routific republishes the route. The loading tick
    /// is preserved — staff having already put the box in the van is our fact, not Routific's.
    /// </summary>
    internal void ApplyPublished(PublishedStopData data)
    {
        SequenceOrder = data.Sequence;
        ExternalStopId = data.ExternalStopId ?? ExternalStopId;
        PlannedArrival = data.PlannedArrival;
        PlannedDeparture = data.PlannedDeparture;
        ActualArrival = data.ActualArrival ?? ActualArrival;
        ActualDeparture = data.ActualDeparture ?? ActualDeparture;
        DistanceFromPreviousKm = data.DistanceFromPreviousKm;
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

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}

/// <summary>
/// Internal carrier for the stop fields a published route supplies.
/// </summary>
internal readonly record struct PublishedStopData(
    Guid DeliveryId,
    int Sequence,
    string? ExternalStopId,
    TimeOnly? PlannedArrival,
    TimeOnly? PlannedDeparture,
    TimeOnly? ActualArrival,
    TimeOnly? ActualDeparture,
    double? DistanceFromPreviousKm);
