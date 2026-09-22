using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A sellable item in Barkfield Road's catalog, mirrored from the Square catalog.
/// </summary>
/// <remarks>
/// This is catalog data only and is shared across every customer. Quantity belongs to the
/// line that references a product (<see cref="SubscriptionItem"/>, <see cref="RotationGroupItem"/>,
/// <see cref="SubscriptionAddOn"/>), never to the product itself.
///
/// Rows are created on demand: when staff pick a Square catalog item we have not seen before,
/// it is upserted here and then referenced by id from that point on.
/// </remarks>
public class Product
{
    public Guid Id { get; private set; }

    /// <summary>Identifier of the backing Square catalog object. Unique across the catalog.</summary>
    public string SquareCatalogObjectId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;
    public string? Sku { get; private set; }

    /// <summary>Current list price. Deliveries snapshot this, so history is not rewritten by repricing.</summary>
    public decimal Price { get; private set; }

    /// <summary>Set false when discontinued. Existing subscription lines are left intact.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Product() { }

    /// <summary>
    /// Creates a local catalog entry mirroring a Square catalog object.
    /// </summary>
    public static Product CreateFromSquare(
        string squareCatalogObjectId,
        string name,
        decimal price,
        string? sku = null)
    {
        ValidateSquareId(squareCatalogObjectId);
        ValidateName(name);
        ValidatePrice(price);

        return new Product
        {
            Id = Guid.NewGuid(),
            SquareCatalogObjectId = squareCatalogObjectId.Trim(),
            Name = name.Trim(),
            Sku = sku?.Trim(),
            Price = price,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Rehydrates a product loaded from the database.
    /// </summary>
    public static Product FromDto(
        Guid id,
        string squareCatalogObjectId,
        string name,
        string? sku,
        decimal price,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid product ID.");

        return new Product
        {
            Id = id,
            SquareCatalogObjectId = squareCatalogObjectId,
            Name = name,
            Sku = sku,
            Price = price,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// Re-syncs name and price from Square. Called when the catalog is refreshed.
    /// </summary>
    public void UpdateFromSquare(string name, decimal price, string? sku = null)
    {
        ValidateName(name);
        ValidatePrice(price);

        Name = name.Trim();
        Price = price;
        Sku = sku?.Trim() ?? Sku;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Reactivate()
    {
        if (IsActive) return;

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateSquareId(string squareCatalogObjectId)
    {
        if (string.IsNullOrWhiteSpace(squareCatalogObjectId))
            throw new DomainException("A Square catalog object ID is required.");
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required and cannot be empty.");

        if (name.Trim().Length > 200)
            throw new DomainException("Product name cannot exceed 200 characters.");
    }

    private static void ValidatePrice(decimal price)
    {
        if (price < 0)
            throw new DomainException("Product price cannot be negative.");
    }
}
