namespace Barkfield.Administration.Application.Services.Products.Models;

/// <summary>
/// One candidate from the unified catalog search — a Square variation, annotated with
/// whether it is already in the local catalog.
/// </summary>
/// <remarks>
/// <para>
/// Staff should not have to know whether a product has been imported yet. They search once,
/// see everything the store sells, and the row itself says whether it is ready to use or
/// needs importing first.
/// </para>
/// <para>
/// Results with <see cref="IsVariablePricing"/> set cannot be imported. They are still
/// returned, because silently hiding a product staff can see in Square would look like a
/// bug; <see cref="CanImport"/> is false and the UI explains why.
/// </para>
/// </remarks>
public class CatalogSearchResultDto
{
    /// <summary>Local product id, or null when this variation has not been imported.</summary>
    public Guid? ProductId { get; set; }

    public string SquareVariationId { get; set; } = string.Empty;
    public string? SquareItemId { get; set; }

    /// <summary>Composed display name — the item name is prepended only when the variation omits it.</summary>
    public string Name { get; set; } = string.Empty;

    public string ItemName { get; set; } = string.Empty;
    public string VariationName { get; set; } = string.Empty;
    public string? Sku { get; set; }

    /// <summary>Square's current price, or null for variable pricing.</summary>
    public decimal? SquarePrice { get; set; }

    /// <summary>The price held locally, or null when not imported.</summary>
    public decimal? LocalPrice { get; set; }

    public bool IsVariablePricing { get; set; }

    /// <summary>False when the product is imported but discontinued locally.</summary>
    public bool IsActive { get; set; }

    public bool IsInCatalog => ProductId.HasValue;

    /// <summary>Not importable when it is already here, or when Square holds no fixed price.</summary>
    public bool CanImport => !IsInCatalog && !IsVariablePricing;

    /// <summary>
    /// The local price has fallen behind Square's. Flags rows a sync would change, so staff
    /// can see stale pricing before it reaches a bill.
    /// </summary>
    public bool PriceIsStale =>
        IsInCatalog && SquarePrice.HasValue && LocalPrice.HasValue && SquarePrice != LocalPrice;
}
