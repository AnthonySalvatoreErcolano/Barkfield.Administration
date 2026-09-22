namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// How confidently an address resolved to coordinates.
/// </summary>
/// <remarks>
/// Worth surfacing to staff: a rural address that lands on a road centreline instead of a
/// driveway will send a driver to roughly the right road and the wrong house.
/// </remarks>
public enum GeocodePrecision
{
    /// <summary>Resolved to the building. Trustworthy.</summary>
    Rooftop = 1,

    /// <summary>Estimated between known address points on a street. Usually fine.</summary>
    Interpolated = 2,

    /// <summary>Resolved only to a street, locality or postcode. Needs review.</summary>
    Approximate = 3,

    /// <summary>A human placed the pin. Treated as authoritative.</summary>
    Manual = 4,

    /// <summary>The geocoder could not resolve the address at all.</summary>
    Failed = 5
}
