using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Products;

/// <summary>
/// Writes for the local product catalog.
/// </summary>
/// <remarks>
/// Takes the domain entity so <see cref="Product.CreateFromSquare"/> and
/// <see cref="Product.SyncFromSquare"/> sit on the only path into the database — including
/// the rule that composes the display name, which would otherwise drift between the import
/// path and the sync path.
/// </remarks>
public interface IProductCommands
{
    Task CreateAsync(Product product, CancellationToken cancellationToken = default);

    Task UpdateAsync(Product product, CancellationToken cancellationToken = default);

    /// <summary>Applies name and price to many products in one round trip. Backs the bulk sync.</summary>
    Task UpdateManyAsync(IReadOnlyCollection<Product> products, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. Returns false if no such product exists.</summary>
    Task<bool> SetActiveAsync(Guid productId, bool isActive, CancellationToken cancellationToken = default);
}
