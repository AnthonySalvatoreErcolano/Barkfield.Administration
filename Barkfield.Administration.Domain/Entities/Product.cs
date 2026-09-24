using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A sellable item in Barkfield Road's catalog, mirrored from a Square item variation.
/// </summary>
/// <remarks>
/// <para>
/// Catalog data only, shared across every customer. Quantity belongs to the line that
/// references a product (<see cref="SubscriptionItem"/>, <see cref="RotationGroupItem"/>,
/// <see cref="SubscriptionAddOn"/>), never to the product itself.
/// </para>
/// <para>
/// <b>This mirrors a Square <i>variation</i>, not an item.</b> In Square an Item ("OC Raw")
/// holds the name and the Variation ("OC Raw 16 lb Venison") holds the price and SKU. The
/// variation is the thing that is actually sold, so it is what a subscription line points at.
/// </para>
/// </remarks>
public class Product
{
    public Guid Id { get; private set; }

    /// <summary>Square variation id. Unique, and what an order line references.</summary>
    public string SquareCatalogObjectId { get; private set; } = string.Empty;

    /// <summary>Square item id — the variation's parent. Used for re-sync and for grouping.</summary>
    public string? SquareItemId { get; private set; }

    /// <summary>Display name, composed from the item and variation names.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>The parent item's name, kept raw so searching a brand still finds it.</summary>
    public string? ItemName { get; private set; }

    /// <summary>The variation's own name, kept raw.</summary>
    public string? VariationName { get; private set; }

    public string? Sku { get; private set; }

    /// <summary>Current list price. Deliveries snapshot this, so repricing never rewrites history.</summary>
    public decimal Price { get; private set; }

    /// <summary>Set false when discontinued. Existing subscription lines are left intact.</summary>
    public bool IsActive { get; private set; }

    /// <summary>When the name and price were last refreshed from Square. Null until first synced.</summary>
    public DateTime? LastSyncedAt { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Product() { }

    /// <summary>
    /// Creates a catalog entry mirroring a Square item variation.
    /// </summary>
    /// <param name="itemName">The parent item's name, e.g. "Open Farm Kibble".</param>
    /// <param name="variationName">The variation's name, e.g. "Wild-Caught Salmon 7 lb".</param>
    public static Product CreateFromSquare(
        string squareVariationId,
        string? squareItemId,
        string itemName,
        string variationName,
        decimal price,
        string? sku = null)
    {
        ValidateSquareId(squareVariationId);
        ValidatePrice(price);

        string displayName = ComposeName(itemName, variationName);
        ValidateName(displayName);

        return new Product
        {
            Id = Guid.NewGuid(),
            SquareCatalogObjectId = squareVariationId.Trim(),
            SquareItemId = squareItemId?.Trim(),
            ItemName = itemName?.Trim(),
            VariationName = variationName?.Trim(),
            Name = displayName,
            Sku = sku?.Trim(),
            Price = price,
            IsActive = true,
            LastSyncedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Product FromDto(
        Guid id,
        string squareCatalogObjectId,
        string? squareItemId,
        string name,
        string? itemName,
        string? variationName,
        string? sku,
        decimal price,
        bool isActive,
        DateTime? lastSyncedAt,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid product ID.");

        return new Product
        {
            Id = id,
            SquareCatalogObjectId = squareCatalogObjectId,
            SquareItemId = squareItemId,
            Name = name,
            ItemName = itemName,
            VariationName = variationName,
            Sku = sku,
            Price = price,
            IsActive = isActive,
            LastSyncedAt = lastSyncedAt,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    /// <summary>
    /// Re-applies name and price from Square. Called by the sync action.
    /// </summary>
    public void SyncFromSquare(string itemName, string variationName, decimal price, string? sku = null)
    {
        ValidatePrice(price);

        string displayName = ComposeName(itemName, variationName);
        ValidateName(displayName);

        ItemName = itemName?.Trim();
        VariationName = variationName?.Trim();
        Name = displayName;
        Price = price;
        Sku = sku?.Trim() ?? Sku;
        LastSyncedAt = DateTime.UtcNow;
        Touch();
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        Touch();
    }

    public void Reactivate()
    {
        if (IsActive) return;

        IsActive = true;
        Touch();
    }

    /// <summary>
    /// Builds the display name from a Square item and variation.
    /// </summary>
    /// <remarks>
    /// Neither name alone works across a real catalog. Barkfield Road's own data shows both
    /// shapes: "OC Raw" + "OC Raw 16 lb Venison" would read as a stutter if joined, while
    /// "Open Farm Kibble" + "Wild-Caught Salmon &amp; Ancient Grains 7 lb" loses the brand if
    /// the item name is dropped. So the item name is prepended only when the variation does
    /// not already carry it.
    /// </remarks>
    public static string ComposeName(string? itemName, string? variationName)
    {
        string item = itemName?.Trim() ?? string.Empty;
        string variation = variationName?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(variation)) return item;
        if (string.IsNullOrEmpty(item)) return variation;

        return variation.Contains(item, StringComparison.OrdinalIgnoreCase)
            ? variation
            : $"{item} — {variation}";
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

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
