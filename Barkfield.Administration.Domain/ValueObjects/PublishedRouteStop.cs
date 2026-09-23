namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// One stop as Routific reports it on a published route: which delivery, where it falls
/// in the run, and the planned and actual times.
/// </summary>
/// <remarks>
/// Deliberately vendor-neutral primitives. The infrastructure layer translates a
/// <c>route.published</c> timeline entry into these; the domain never sees Routific's payload.
/// Only <c>delivering_stop</c> entries become stops — depot legs and idle time are dropped.
/// </remarks>
public sealed record PublishedRouteStop(
    Guid DeliveryId,
    int Sequence,
    string? ExternalStopId = null,
    TimeOnly? PlannedArrival = null,
    TimeOnly? PlannedDeparture = null,
    TimeOnly? ActualArrival = null,
    TimeOnly? ActualDeparture = null,
    double? DistanceFromPreviousKm = null);
