using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A place a customer receives deliveries: the address, its geocoded coordinates, and the
/// driver-facing detail that makes the stop work.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="Customer.Address"/> because routing needs coordinates, drivers need
/// access notes, a customer may have more than one address, and the optimiser needs a service
/// duration per stop — none of which an address value object carries.
/// </para>
/// <para>
/// A location cannot be routed until it has been geocoded. <see cref="Precision"/> is kept so
/// staff can spot a low-confidence result and correct it by hand.
/// </para>
/// </remarks>
public class DeliveryLocation
{
    /// <summary>Fallback stop duration when none is set, in minutes.</summary>
    public const int DefaultServiceDurationMinutes = 5;

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }

    /// <summary>Staff-facing label, e.g. "Home" or "Shop".</summary>
    public string Label { get; private set; } = string.Empty;

    public Address Address { get; private set; } = null!;

    /// <summary>Null until geocoded. Required before this location can go on a route.</summary>
    public GeoPoint? Coordinates { get; private set; }

    public GeocodePrecision? Precision { get; private set; }
    public DateTime? GeocodedAt { get; private set; }

    /// <summary>Driver-facing instructions — gate codes, "leave at side door", "dog in yard".</summary>
    public string? AccessNotes { get; private set; }

    /// <summary>How long the stop takes. Feeds the optimiser's per-visit duration.</summary>
    public int ServiceDurationMinutes { get; private set; }

    /// <summary>Customer's preferred delivery window, if any.</summary>
    public TimeWindow? PreferredWindow { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>True when this location has everything the optimiser needs.</summary>
    public bool IsRoutable => IsActive && Coordinates is not null;

    /// <summary>True when the geocode resolved poorly enough that staff should check it.</summary>
    public bool NeedsGeocodeReview =>
        Precision is null or GeocodePrecision.Approximate or GeocodePrecision.Failed;

    private DeliveryLocation() { }

    public static DeliveryLocation Create(
        Guid customerId,
        string label,
        Address address,
        bool isDefault = false,
        string? accessNotes = null,
        int? serviceDurationMinutes = null,
        TimeWindow? preferredWindow = null)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("A delivery location must belong to a valid customer.");

        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException("Delivery location label is required.");

        ArgumentNullException.ThrowIfNull(address);

        return new DeliveryLocation
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Label = label.Trim(),
            Address = address,
            AccessNotes = accessNotes?.Trim(),
            ServiceDurationMinutes = ValidateServiceDuration(serviceDurationMinutes ?? DefaultServiceDurationMinutes),
            PreferredWindow = preferredWindow,
            IsDefault = isDefault,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static DeliveryLocation FromDto(
        Guid id,
        Guid customerId,
        string label,
        Address address,
        GeoPoint? coordinates,
        GeocodePrecision? precision,
        DateTime? geocodedAt,
        string? accessNotes,
        int serviceDurationMinutes,
        TimeWindow? preferredWindow,
        bool isDefault,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid delivery location ID.");

        return new DeliveryLocation
        {
            Id = id,
            CustomerId = customerId,
            Label = label,
            Address = address,
            Coordinates = coordinates,
            Precision = precision,
            GeocodedAt = geocodedAt,
            AccessNotes = accessNotes,
            ServiceDurationMinutes = serviceDurationMinutes,
            PreferredWindow = preferredWindow,
            IsDefault = isDefault,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// Records the result of geocoding this address.
    /// </summary>
    public void SetCoordinates(GeoPoint coordinates, GeocodePrecision precision)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        Coordinates = coordinates;
        Precision = precision;
        GeocodedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Records that geocoding failed, leaving the location unroutable until a human intervenes.
    /// </summary>
    public void MarkGeocodeFailed()
    {
        Coordinates = null;
        Precision = GeocodePrecision.Failed;
        GeocodedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Places the pin by hand, overriding whatever the geocoder produced.
    /// </summary>
    public void SetCoordinatesManually(GeoPoint coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        Coordinates = coordinates;
        Precision = GeocodePrecision.Manual;
        GeocodedAt = DateTime.UtcNow;
        Touch();
    }

    /// <summary>
    /// Changes the address. Coordinates are cleared, because they now describe the old one —
    /// the caller must re-geocode before this location can be routed again.
    /// </summary>
    public void UpdateAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
        Coordinates = null;
        Precision = null;
        GeocodedAt = null;
        Touch();
    }

    public void UpdateAccessNotes(string? accessNotes)
    {
        AccessNotes = string.IsNullOrWhiteSpace(accessNotes) ? null : accessNotes.Trim();
        Touch();
    }

    public void UpdateServiceDuration(int minutes)
    {
        ServiceDurationMinutes = ValidateServiceDuration(minutes);
        Touch();
    }

    public void SetPreferredWindow(TimeWindow? window)
    {
        PreferredWindow = window;
        Touch();
    }

    public void Rename(string label)
    {
        if (string.IsNullOrWhiteSpace(label))
            throw new DomainException("Delivery location label is required.");

        Label = label.Trim();
        Touch();
    }

    internal void MarkDefault(bool isDefault)
    {
        IsDefault = isDefault;
        Touch();
    }

    public void Deactivate()
    {
        IsActive = false;
        Touch();
    }

    public void Reactivate()
    {
        IsActive = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private static int ValidateServiceDuration(int minutes)
    {
        if (minutes is < 0 or > 480)
            throw new DomainException("Service duration must be between 0 and 480 minutes.");

        return minutes;
    }
}
