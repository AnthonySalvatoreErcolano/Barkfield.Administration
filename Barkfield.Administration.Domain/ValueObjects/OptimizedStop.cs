namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// One entry in an optimiser's answer: which delivery, and when the van is expected there.
/// </summary>
/// <remarks>
/// Deliberately vendor-neutral. The domain accepts these plain values from any optimiser
/// implementation and never learns which engine produced them.
/// </remarks>
public sealed record OptimizedStop(
    Guid DeliveryId,
    TimeOnly? EstimatedArrival,
    TimeOnly? EstimatedDeparture);
