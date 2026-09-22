using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A location routes start and end at — the store itself.
/// </summary>
/// <remarks>
/// Modelled as an entity rather than configuration so a second location later is a row,
/// not a migration. The optimiser uses this as the fixed first and last node of every route.
/// </remarks>
public class Depot
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public Address Address { get; private set; } = null!;
    public GeoPoint Coordinates { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Depot() { }

    public static Depot Create(string name, Address address, GeoPoint coordinates)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Depot name is required.");

        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(coordinates);

        return new Depot
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Address = address,
            Coordinates = coordinates,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Depot FromDto(
        Guid id,
        string name,
        Address address,
        GeoPoint coordinates,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid depot ID.");

        return new Depot
        {
            Id = id,
            Name = name,
            Address = address,
            Coordinates = coordinates,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Relocate(Address address, GeoPoint coordinates)
    {
        ArgumentNullException.ThrowIfNull(address);
        ArgumentNullException.ThrowIfNull(coordinates);

        Address = address;
        Coordinates = coordinates;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Depot name is required.");

        Name = name.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }
}
