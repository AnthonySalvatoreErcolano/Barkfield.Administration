namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Lifecycle of a delivery route, mirroring the states Routific reports.
/// </summary>
/// <remarks>
/// Phase 1 routes are built in Routific and arrive here already published, so this
/// tracks an external lifecycle rather than one this system drives.
/// </remarks>
public enum RouteStatus
{
    /// <summary>Built in Routific but not yet released to a driver.</summary>
    Planned = 1,

    /// <summary>Released to the driver. This is the state routes normally reach us in.</summary>
    Published = 2,

    /// <summary>The driver is working the route.</summary>
    Executing = 3,

    /// <summary>Every stop has been attempted.</summary>
    Completed = 4,

    /// <summary>Called off before it ran.</summary>
    Canceled = 5
}
