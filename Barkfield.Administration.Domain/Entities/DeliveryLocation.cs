using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A place a customer receives deliveries: the address, the driver-facing detail that makes
/// the stop work, and how long it takes.
/// </summary>
/// <remarks>
/// <para>
/// Separate from <see cref="Customer.Address"/> because a customer may have more than one
/// delivery address, drivers need access notes, and the route planner needs a service duration
/// and a delivery window per stop.
/// </para>
/// <para>
/// Every field here maps onto an order pushed to the routing provider: address, phone,
/// <c>instructions</c>, <c>duration</c> and <c>timeWindows</c>.
/// </para>
/// <para>
/// Coordinates are optional. Routific geocodes from the address string, so nothing here blocks
/// on having them — they are stored when a provider hands them back, and become required only
/// if routing is brought in-house.
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

    /// <summary>Set when a routing provider returns coordinates for this address. Not required.</summary>
    public GeoPoint? Coordinates { get; private set; }

    /// <summary>Driver-facing instructions — gate codes, "leave at side door", "dog in yard".</summary>
    public string? AccessNotes { get; private set; }

    /// <summary>How long the stop takes. Sent as the order's service duration.</summary>
    public int ServiceDurationMinutes { get; private set; }

    /// <summary>Customer's preferred delivery window, if any.</summary>
    public TimeWindow? PreferredWindow { get; private set; }

    public bool IsDefault { get; private set; }
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

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
    /// Records coordinates handed back by a routing provider.
    /// </summary>
    public void SetCoordinates(GeoPoint coordinates)
    {
        ArgumentNullException.ThrowIfNull(coordinates);

        Coordinates = coordinates;
        Touch();
    }

    /// <summary>
    /// Changes the address. Coordinates are cleared, because they describe the old one.
    /// </summary>
    public void UpdateAddress(Address address)
    {
        ArgumentNullException.ThrowIfNull(address);

        Address = address;
        Coordinates = null;
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
