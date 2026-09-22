namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Lifecycle of a delivery route.
/// </summary>
public enum RouteStatus
{
    /// <summary>Staff are still assigning deliveries. Stop order is not meaningful yet.</summary>
    Draft = 1,

    /// <summary>The optimiser has ordered the stops. Still editable.</summary>
    Optimized = 2,

    /// <summary>Released to the driver app. Still editable, but changes need re-publishing.</summary>
    Published = 3,

    /// <summary>The driver has started the run.</summary>
    InProgress = 4,

    /// <summary>Every stop has been attempted.</summary>
    Completed = 5,

    /// <summary>Called off before it ran.</summary>
    Canceled = 6
}
