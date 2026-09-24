using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Products;

/// <summary>
/// Writes for the local product catalog.
/// </summary>
public class ProductCommands : IProductCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public ProductCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        const string sql = @"
            INSERT INTO dbo.Products
                (Id, SquareCatalogObjectId, SquareItemId, Name, ItemName, VariationName,
                 Sku, Price, IsActive, LastSyncedAt, CreatedAt)
            VALUES
                (@Id, @SquareCatalogObjectId, @SquareItemId, @Name, @ItemName, @VariationName,
                 @Sku, @Price, @IsActive, @LastSyncedAt, @CreatedAt);";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(product), cancellationToken);
    }

    public async Task UpdateAsync(Product product, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(product);

        await _sqlExecutor.ExecuteAsync(UpdateStatement, ToParameters(product), cancellationToken);
    }

    /// <summary>
    /// Applies the same update to many products in one round trip.
    /// </summary>
    /// <remarks>
    /// Dapper executes this once per item over a single connection rather than opening one
    /// per product. A sync touching a few hundred rows is the only caller, and it runs while
    /// someone is waiting for the page — the round trips are what would be felt.
    /// </remarks>
    public async Task UpdateManyAsync(
        IReadOnlyCollection<Product> products,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(products);

        if (products.Count == 0) return;

        var parameters = products.Select(ToParameters).ToList();

        await _sqlExecutor.ExecuteAsync(UpdateStatement, parameters, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(
        Guid productId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Products
               SET IsActive  = @IsActive,
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @ProductId;";

        int affected = await _sqlExecutor.ExecuteAsync(
            sql, new { ProductId = productId, IsActive = isActive }, cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// Shared by the single and bulk updates so the two cannot drift.
    /// </summary>
    /// <remarks>
    /// SquareCatalogObjectId is deliberately absent. It is the identity of the mirrored
    /// variation and carries a unique index; changing it would not be an edit, it would be a
    /// different product.
    /// </remarks>
    private const string UpdateStatement = @"
        UPDATE dbo.Products
           SET SquareItemId  = @SquareItemId,
               Name          = @Name,
               ItemName      = @ItemName,
               VariationName = @VariationName,
               Sku           = @Sku,
               Price         = @Price,
               IsActive      = @IsActive,
               LastSyncedAt  = @LastSyncedAt,
               UpdatedAt     = @UpdatedAt
         WHERE Id = @Id;";

    private static object ToParameters(Product product) => new
    {
        product.Id,
        product.SquareCatalogObjectId,
        product.SquareItemId,
        product.Name,
        product.ItemName,
        product.VariationName,
        product.Sku,
        product.Price,
        product.IsActive,
        product.LastSyncedAt,
        product.CreatedAt,
        UpdatedAt = product.UpdatedAt ?? DateTime.UtcNow
    };
}
