using Barkfield.Administration.Application.Common;

namespace Barkfield.Administration.Application.DataAccess.Products;

public interface IProductQueries
{
    Task<PagedResult<ProductDto>> SearchAsync(ProductFilter filter, CancellationToken cancellationToken = default);

    Task<ProductDto?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>Finds an already-imported product by its Square variation id. Used to keep imports idempotent.</summary>
    Task<ProductDto?> GetBySquareVariationIdAsync(string squareVariationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up many variation ids at once, including inactive ones.
    /// </summary>
    /// <remarks>
    /// Backs the unified search: Square returns candidates, and this says which of them are
    /// already in the local catalog so the picker can mark them rather than offering a
    /// duplicate import.
    /// </remarks>
    Task<IReadOnlyCollection<ProductDto>> GetBySquareVariationIdsAsync(
        IEnumerable<string> squareVariationIds,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Looks up several products by id, for the catalog dictionary a delivery needs when it
    /// snapshots names and prices.
    /// </summary>
    Task<IReadOnlyCollection<ProductDto>> GetByIdsAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default);

    /// <summary>Every active product, for the bulk re-sync.</summary>
    Task<IReadOnlyCollection<ProductDto>> GetAllActiveAsync(CancellationToken cancellationToken = default);
}
