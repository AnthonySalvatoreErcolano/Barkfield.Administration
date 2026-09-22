using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// A geocoded coordinate. Required before a location can be routed.
/// </summary>
/// <remarks>
/// Note the ordering trap: this type is latitude-first, as coordinates are normally written,
/// but OSRM's API takes <c>lon,lat</c>. The infrastructure adapter is responsible for the swap.
/// </remarks>
public sealed record GeoPoint
{
    private const double EarthRadiusKm = 6371.0;

    public double Latitude { get; }
    public double Longitude { get; }

    private GeoPoint(double latitude, double longitude)
    {
        if (double.IsNaN(latitude) || latitude is < -90 or > 90)
            throw new DomainException($"Latitude must be between -90 and 90 (got {latitude}).");

        if (double.IsNaN(longitude) || longitude is < -180 or > 180)
            throw new DomainException($"Longitude must be between -180 and 180 (got {longitude}).");

        Latitude = latitude;
        Longitude = longitude;
    }

    public static GeoPoint Create(double latitude, double longitude) => new(latitude, longitude);

    /// <summary>
    /// Great-circle distance in kilometres. Used only for the approximate fallback matrix when
    /// the road-network service is unreachable — it ignores roads, water and one-ways.
    /// </summary>
    public double DistanceKmTo(GeoPoint other)
    {
        ArgumentNullException.ThrowIfNull(other);

        double lat1 = DegreesToRadians(Latitude);
        double lat2 = DegreesToRadians(other.Latitude);
        double deltaLat = DegreesToRadians(other.Latitude - Latitude);
        double deltaLon = DegreesToRadians(other.Longitude - Longitude);

        double a = Math.Sin(deltaLat / 2) * Math.Sin(deltaLat / 2)
                 + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2) * Math.Sin(deltaLon / 2);

        return EarthRadiusKm * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180.0;

    public override string ToString() => $"{Latitude:F6},{Longitude:F6}";
}
