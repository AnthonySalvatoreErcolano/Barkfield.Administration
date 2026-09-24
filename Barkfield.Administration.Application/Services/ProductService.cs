using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Products.Models;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// The local product catalog and its relationship to Square.
/// </summary>
/// <remarks>
/// <para>
/// Everything Barkfield Road sells lives in Square. This application keeps a local mirror of
/// the subset that appears on subscriptions, for three reasons: a subscription line needs a
/// stable row to reference, the packing and dispatch screens must work without a round trip
/// to Square for every line, and a delivery's history must survive a product being renamed
/// or discontinued upstream.
/// </para>
/// <para>
/// The mirror is deliberately one-way. Nothing here writes to Square's catalog — staff
/// maintain products in the point of sale, as they already do.
/// </para>
/// </remarks>
public class ProductService
{
    private readonly IProductQueries _productQueries;
    private readonly IProductCommands _productCommands;
    private readonly ISquareCatalogService _squareCatalog;

    public ProductService(
        IProductQueries productQueries,
        IProductCommands productCommands,
        ISquareCatalogService squareCatalog)
    {
        _productQueries = productQueries;
        _productCommands = productCommands;
        _squareCatalog = squareCatalog;
    }

    public Task<PagedResult<ProductDto>> SearchAsync(ProductFilter filter, CancellationToken cancellationToken = default) =>
        _productQueries.SearchAsync(filter, cancellationToken);

    public async Task<ProductDto> GetAsync(Guid productId, CancellationToken cancellationToken = default) =>
        await _productQueries.GetByIdAsync(productId, cancellationToken)
            ?? throw new NotFoundException($"Product with ID '{productId}' was not found.");

    /// <summary>
    /// Searches Square's catalog and marks which results are already imported.
    /// </summary>
    /// <remarks>
    /// The Square call is the authority on what exists; the local lookup only annotates. That
    /// ordering matters — searching locally first would hide every product staff have not yet
    /// imported, which is precisely the set they are usually looking for.
    /// </remarks>
    public async Task<IReadOnlyCollection<CatalogSearchResultDto>> SearchSquareCatalogAsync(
        string searchText,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            throw new ValidationException("A search term is required.");
        }

        IReadOnlyCollection<SquareVariationDto> candidates =
            await _squareCatalog.SearchVariationsAsync(searchText.Trim(), limit, cancellationToken);

        if (candidates.Count == 0) return [];

        IReadOnlyCollection<ProductDto> imported = await _productQueries.GetBySquareVariationIdsAsync(
            candidates.Select(c => c.VariationId), cancellationToken);

        Dictionary<string, ProductDto> byVariationId =
            imported.ToDictionary(p => p.SquareCatalogObjectId, StringComparer.Ordinal);

        return candidates.Select(candidate =>
        {
            byVariationId.TryGetValue(candidate.VariationId, out ProductDto? local);

            return new CatalogSearchResultDto
            {
                ProductId = local?.Id,
                SquareVariationId = candidate.VariationId,
                SquareItemId = candidate.ItemId,
                Name = Product.ComposeName(candidate.ItemName, candidate.VariationName),
                ItemName = candidate.ItemName,
                VariationName = candidate.VariationName,
                Sku = candidate.Sku,
                SquarePrice = candidate.Price,
                LocalPrice = local?.Price,
                IsVariablePricing = candidate.IsVariablePricing,
                IsActive = local?.IsActive ?? false
            };
        }).ToList();
    }

    /// <summary>
    /// Imports a Square variation into the local catalog. Idempotent.
    /// </summary>
    /// <remarks>
    /// Importing something already here returns the existing product rather than failing —
    /// two staff members importing the same product from two screens is an ordinary thing to
    /// happen, not an error worth surfacing. If the existing row was discontinued, importing
    /// it again restores it, which is what clicking "add" plainly means.
    /// </remarks>
    public async Task<Guid> ImportAsync(string squareVariationId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(squareVariationId))
        {
            throw new ValidationException("A Square variation ID is required.");
        }

        string variationId = squareVariationId.Trim();

        ProductDto? existing = await _productQueries.GetBySquareVariationIdAsync(variationId, cancellationToken);

        SquareVariationDto variation = await _squareCatalog.GetVariationAsync(variationId, cancellationToken)
            ?? throw new NotFoundException($"Square has no catalog variation with ID '{variationId}'.");

        // Variable-priced products are weighed or quoted at the counter, so Square holds no
        // price for them. A subscription line needs a figure to bill against, and inventing
        // one here would quietly overcharge somebody.
        if (variation.IsVariablePricing || variation.Price is null)
        {
            string label = Product.ComposeName(variation.ItemName, variation.VariationName);

            throw new ValidationException(
                $"'{label}' is priced per sale in Square and cannot be added to a subscription. " +
                "Give it a fixed price in Square first.");
        }

        if (existing is not null)
        {
            // Re-importing doubles as a refresh: the caller has just seen this product in
            // Square, so taking its current name and price is the least surprising outcome.
            Product product = ToEntity(existing);

            product.SyncFromSquare(variation.ItemName, variation.VariationName, variation.Price.Value, variation.Sku);
            product.Reactivate();

            await _productCommands.UpdateAsync(product, cancellationToken);

            return product.Id;
        }

        Product created = Product.CreateFromSquare(
            variation.VariationId,
            variation.ItemId,
            variation.ItemName,
            variation.VariationName,
            variation.Price.Value,
            variation.Sku);

        await _productCommands.CreateAsync(created, cancellationToken);

        return created.Id;
    }

    /// <summary>
    /// Refreshes one product's name, price and SKU from Square.
    /// </summary>
    public async Task SyncAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        ProductDto dto = await GetAsync(productId, cancellationToken);

        SquareVariationDto variation =
            await _squareCatalog.GetVariationAsync(dto.SquareCatalogObjectId, cancellationToken)
            ?? throw new NotFoundException(
                $"'{dto.Name}' no longer exists in Square (variation '{dto.SquareCatalogObjectId}').");

        if (variation.IsVariablePricing || variation.Price is null)
        {
            throw new ValidationException(
                $"'{dto.Name}' has been changed to variable pricing in Square. Its local price was left unchanged.");
        }

        Product product = ToEntity(dto);

        product.SyncFromSquare(variation.ItemName, variation.VariationName, variation.Price.Value, variation.Sku);

        await _productCommands.UpdateAsync(product, cancellationToken);
    }

    /// <summary>
    /// Refreshes every active product against Square in one pass.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prices change in the point of sale without anything telling us, so this exists to be
    /// run before a billing day rather than on a schedule — staff should know when the
    /// catalog last moved, and a silent background job takes that away from them.
    /// </para>
    /// <para>
    /// Products Square no longer returns are reported, not deleted. Their rows are referenced
    /// by subscription lines and by delivery history, and a product vanishing from Square is
    /// as often a staff member reorganising the catalog as a genuine discontinuation.
    /// </para>
    /// </remarks>
    public async Task<CatalogSyncResult> SyncAllAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<ProductDto> products = await _productQueries.GetAllActiveAsync(cancellationToken);

        if (products.Count == 0) return new CatalogSyncResult(0, 0, 0, []);

        IReadOnlyCollection<SquareVariationDto> variations = await _squareCatalog.GetVariationsAsync(
            products.Select(p => p.SquareCatalogObjectId), cancellationToken);

        Dictionary<string, SquareVariationDto> byVariationId =
            variations.ToDictionary(v => v.VariationId, StringComparer.Ordinal);

        var changed = new List<Product>();
        var missing = new List<string>();
        int unchanged = 0;

        foreach (ProductDto dto in products)
        {
            // A product switched to variable pricing in Square has no price to copy. Treated
            // like a missing one: reported, left alone, never zeroed out.
            if (!byVariationId.TryGetValue(dto.SquareCatalogObjectId, out SquareVariationDto? variation)
                || variation.IsVariablePricing
                || variation.Price is null)
            {
                missing.Add(dto.Name);
                continue;
            }

            string composedName = Product.ComposeName(variation.ItemName, variation.VariationName);

            if (composedName == dto.Name && variation.Price.Value == dto.Price && variation.Sku == dto.Sku)
            {
                unchanged++;
                continue;
            }

            Product product = ToEntity(dto);

            product.SyncFromSquare(variation.ItemName, variation.VariationName, variation.Price.Value, variation.Sku);

            changed.Add(product);
        }

        if (changed.Count > 0)
        {
            await _productCommands.UpdateManyAsync(changed, cancellationToken);
        }

        return new CatalogSyncResult(products.Count, changed.Count, unchanged, missing);
    }

    /// <summary>
    /// Discontinues a product. A soft delete — subscription lines and delivery history keep
    /// pointing at it, and it simply stops appearing in pickers.
    /// </summary>
    public async Task DeactivateAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!await _productCommands.SetActiveAsync(productId, false, cancellationToken))
        {
            throw new NotFoundException($"Product with ID '{productId}' was not found.");
        }
    }

    public async Task ReactivateAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        if (!await _productCommands.SetActiveAsync(productId, true, cancellationToken))
        {
            throw new NotFoundException($"Product with ID '{productId}' was not found.");
        }
    }

    /// <summary>Live from Square — discounts are never mirrored locally.</summary>
    public Task<IReadOnlyCollection<SquareDiscountDto>> GetDiscountsAsync(CancellationToken cancellationToken = default) =>
        _squareCatalog.ListDiscountsAsync(cancellationToken);

    private static Product ToEntity(ProductDto dto) => Product.FromDto(
        dto.Id,
        dto.SquareCatalogObjectId,
        dto.SquareItemId,
        dto.Name,
        dto.ItemName,
        dto.VariationName,
        dto.Sku,
        dto.Price,
        dto.IsActive,
        dto.LastSyncedAt,
        dto.CreatedAt,
        dto.UpdatedAt);
}
