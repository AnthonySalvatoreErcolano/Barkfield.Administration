using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Products;

public class ProductQueries : IProductQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// Maps an accepted sort key to a fixed ORDER BY expression.
    /// </summary>
    /// <remarks>
    /// The client's sort key is never concatenated into SQL. It is looked up here, and an
    /// unrecognised key cannot reach the database at all — parameters cannot be used for
    /// column names, so a free-text ORDER BY would be an injection hole.
    /// </remarks>
    private static readonly Dictionary<string, string[]> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["p.Name"],
        ["price"] = ["p.Price"],
        ["sku"] = ["p.Sku"],
        ["createdAt"] = ["p.CreatedAt"],
        ["lastSyncedAt"] = ["p.LastSyncedAt"]
    };

    /// <summary>
    /// Builds the ORDER BY clause, applying the direction to every column and appending Id.
    /// </summary>
    /// <remarks>
    /// Id is the tiebreaker so paging is stable: rows sharing a sort value — and products
    /// sharing a price are common — otherwise have no guaranteed order and can appear on two
    /// pages or none.
    /// </remarks>
    private static string BuildOrderBy(string sortKey, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";
        var columns = SortColumns[sortKey].Select(column => $"{column} {direction}");

        return string.Join(", ", columns) + ", p.Id";
    }

    /// <summary>
    /// The column list every read shares, so the paged list and a single fetch cannot drift.
    /// </summary>
    private const string SelectColumns = @"
        p.Id,
        p.SquareCatalogObjectId,
        p.SquareItemId,
        p.Name,
        p.ItemName,
        p.VariationName,
        p.Sku,
        p.Price,
        p.IsActive,
        p.LastSyncedAt,
        p.CreatedAt,
        p.UpdatedAt";

    /// <summary>
    /// How many subscription lines reference the product, across all three ways one can.
    /// </summary>
    /// <remarks>
    /// A product can be a fixed item on a subscription, a member of a rotation group, or a
    /// one-off add-on. All three count as "in use" — the number exists so staff are not
    /// discontinuing something that is about to ship.
    /// </remarks>
    private const string UsageCountColumn = @"
        (SELECT COUNT(1) FROM dbo.SubscriptionItems si WHERE si.ProductId = p.Id)
      + (SELECT COUNT(1) FROM dbo.RotationGroupItems rgi WHERE rgi.ProductId = p.Id)
      + (SELECT COUNT(1) FROM dbo.SubscriptionAddOns sa WHERE sa.ProductId = p.Id) AS SubscriptionUsageCount";

    public ProductQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PagedResult<ProductDto>> SearchAsync(
        ProductFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var where = new StringBuilder(" FROM dbo.Products p WHERE 1 = 1 ");
        var parameters = new DynamicParameters();

        if (!filter.IncludeInactive)
        {
            where.Append(" AND p.IsActive = 1 ");
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            // ItemName is searched alongside Name because the composed name drops the brand
            // whenever the variation already repeats it. Without this, typing "Open Farm"
            // would miss variations named only "Wild-Caught Salmon 7 lb".
            where.Append(@" AND (p.Name LIKE @SearchTerm
                             OR p.ItemName LIKE @SearchTerm
                             OR p.VariationName LIKE @SearchTerm
                             OR p.Sku LIKE @SearchTerm) ");

            // Escape LIKE wildcards so a literal % or _ in the search box does not match everything.
            string term = filter.SearchTerm.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            parameters.Add("SearchTerm", $"%{term}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.SquareItemId))
        {
            where.Append(" AND p.SquareItemId = @SquareItemId ");
            parameters.Add("SquareItemId", filter.SquareItemId.Trim());
        }

        string orderBy = BuildOrderBy(filter.EffectiveSortBy, filter.SortDescending);

        parameters.Add("Skip", filter.Skip);
        parameters.Add("PageSize", filter.PageSize);

        // Count and page in one round trip.
        string sql = $@"
            SELECT COUNT(1) {where};

            SELECT {SelectColumns},
                   {UsageCountColumn}
            {where}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        int totalCount = await reader.ReadSingleAsync<int>();
        var items = await reader.ReadAsync<ProductDto>();

        return new PagedResult<ProductDto>(
            items.ToList(),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    public async Task<ProductDto?> GetByIdAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        string sql = $@"
            SELECT {SelectColumns},
                   {UsageCountColumn}
            FROM dbo.Products p
            WHERE p.Id = @ProductId;";

        var products = await _sqlExecutor.QueryAsync<ProductDto>(
            sql, new { ProductId = productId }, cancellationToken);

        return products.FirstOrDefault();
    }

    public async Task<ProductDto?> GetBySquareVariationIdAsync(
        string squareVariationId,
        CancellationToken cancellationToken = default)
    {
        // Inactive rows are deliberately included: importing a product that was previously
        // discontinued must find the existing row and restore it, not create a duplicate
        // that would collide on the unique index.
        string sql = $@"
            SELECT {SelectColumns},
                   {UsageCountColumn}
            FROM dbo.Products p
            WHERE p.SquareCatalogObjectId = @SquareVariationId;";

        var products = await _sqlExecutor.QueryAsync<ProductDto>(
            sql, new { SquareVariationId = squareVariationId }, cancellationToken);

        return products.FirstOrDefault();
    }

    public async Task<IReadOnlyCollection<ProductDto>> GetBySquareVariationIdsAsync(
        IEnumerable<string> squareVariationIds,
        CancellationToken cancellationToken = default)
    {
        var ids = squareVariationIds?.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList() ?? [];

        if (ids.Count == 0) return [];

        // Dapper expands the list into an IN clause. The ids come from Square's own response,
        // never from the client, but they are still parameterised rather than interpolated.
        string sql = $@"
            SELECT {SelectColumns},
                   {UsageCountColumn}
            FROM dbo.Products p
            WHERE p.SquareCatalogObjectId IN @Ids;";

        var products = await _sqlExecutor.QueryAsync<ProductDto>(sql, new { Ids = ids }, cancellationToken);

        return products.ToList();
    }

    public async Task<IReadOnlyCollection<ProductDto>> GetByIdsAsync(
        IEnumerable<Guid> productIds,
        CancellationToken cancellationToken = default)
    {
        var ids = productIds?.Where(id => id != Guid.Empty).Distinct().ToList() ?? [];

        if (ids.Count == 0) return [];

        // Inactive products are included deliberately: a delivery already referencing a
        // discontinued product still has to be able to snapshot its name and price.
        string sql = $@"
            SELECT {SelectColumns},
                   0 AS SubscriptionUsageCount
            FROM dbo.Products p
            WHERE p.Id IN @Ids;";

        var products = await _sqlExecutor.QueryAsync<ProductDto>(sql, new { Ids = ids }, cancellationToken);

        return products.ToList();
    }

    public async Task<IReadOnlyCollection<ProductDto>> GetAllActiveAsync(CancellationToken cancellationToken = default)
    {
        // The usage count is skipped here. This backs the bulk sync, which reads the whole
        // catalog and cares only about names and prices — three correlated subqueries per row
        // would be paid for nothing.
        string sql = $@"
            SELECT {SelectColumns},
                   0 AS SubscriptionUsageCount
            FROM dbo.Products p
            WHERE p.IsActive = 1
            ORDER BY p.Name, p.Id;";

        var products = await _sqlExecutor.QueryAsync<ProductDto>(sql, null, cancellationToken);

        return products.ToList();
    }
}
