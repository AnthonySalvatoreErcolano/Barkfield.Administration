namespace Barkfield.Administration.Application.DataAccess.Products;

/// <summary>
/// Search, sort and pagination for the product catalog.
/// </summary>
/// <param name="SearchTerm">Matched against the display name, the parent item name and the SKU.</param>
/// <param name="IncludeInactive">Discontinued products are excluded unless this is set.</param>
/// <param name="SquareItemId">Narrows to one Square item's variations — the sizes of a single product.</param>
public record ProductFilter(
    string? SearchTerm = null,
    bool IncludeInactive = false,
    string? SquareItemId = null,
    int PageNumber = 1,
    int PageSize = 25,
    string? SortBy = null,
    bool SortDescending = false)
{
    /// <summary>
    /// Sort keys the API accepts. Anything else falls back to <see cref="DefaultSort"/>.
    /// </summary>
    /// <remarks>
    /// The key never reaches SQL as text — the data layer maps it to a fixed column
    /// expression, because a free-text ORDER BY would be an injection hole.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedSortKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "price", "sku", "createdAt", "lastSyncedAt"
        };

    public const string DefaultSort = "name";

    // Clamped so a client cannot ask for the entire table in one page.
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 200);
    public int PageNumber { get; init; } = Math.Max(PageNumber, 1);

    public int Skip => (PageNumber - 1) * PageSize;

    public string EffectiveSortBy =>
        SortBy is not null && AllowedSortKeys.Contains(SortBy) ? SortBy : DefaultSort;
}
