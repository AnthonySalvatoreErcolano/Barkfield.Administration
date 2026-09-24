namespace Barkfield.Administration.Application.DataAccess.Products;

/// <summary>
/// A product in the local catalog — one mirrored Square item variation.
/// </summary>
public class ProductDto
{
    public Guid Id { get; set; }

    /// <summary>Square variation id. Unique, and what a Square order line references.</summary>
    public string SquareCatalogObjectId { get; set; } = string.Empty;

    /// <summary>Square item id — the variation's parent.</summary>
    public string? SquareItemId { get; set; }

    /// <summary>Composed display name. What appears in a picker and on a packing list.</summary>
    public string Name { get; set; } = string.Empty;

    public string? ItemName { get; set; }
    public string? VariationName { get; set; }
    public string? Sku { get; set; }
    public decimal Price { get; set; }
    public bool IsActive { get; set; }
    public DateTime? LastSyncedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// How many subscription lines point at this product. Shown before deactivating, so
    /// staff can see what discontinuing it would affect.
    /// </summary>
    public int SubscriptionUsageCount { get; set; }
}
