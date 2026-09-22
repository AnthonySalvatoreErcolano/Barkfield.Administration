namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Which travel-time data produced a route's stop order. Recorded so staff know how far to
/// trust the ETAs.
/// </summary>
public enum RouteOptimizationSource
{
    /// <summary>Real road-network travel times. Normal operation.</summary>
    RoadNetwork = 1,

    /// <summary>
    /// Straight-line fallback, used when the road-network service was unreachable. The ordering
    /// is a reasonable guess but ignores roads and water; ETAs are rough. Surface this in the UI.
    /// </summary>
    Approximate = 2
}
