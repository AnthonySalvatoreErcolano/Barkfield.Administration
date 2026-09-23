namespace Barkfield.Administration.Application.DataAccess.Customers;

/// <summary>
/// Search, sort and pagination for the customer dashboard.
/// </summary>
/// <param name="SearchTerm">Matched against first name, last name, email and phone.</param>
/// <param name="Email">Exact match, for duplicate checks.</param>
/// <param name="HasSquareAccount">Filters on whether a Square profile is linked. Surfaces unsynced customers.</param>
/// <param name="IncludeInactive">Deactivated customers are excluded unless this is set.</param>
public record CustomerFilter(
    string? SearchTerm = null,
    string? Email = null,
    bool? HasSquareAccount = null,
    bool IncludeInactive = false,
    int PageNumber = 1,
    int PageSize = 25,
    string? SortBy = null,
    bool SortDescending = false)
{
    /// <summary>
    /// Sort keys the API accepts. Anything else falls back to <see cref="DefaultSort"/>.
    /// </summary>
    /// <remarks>
    /// The sort key never reaches SQL as text — the data layer maps it to a fixed column
    /// expression. A free-text ORDER BY would be an injection hole.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedSortKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "name", "email", "createdAt", "city", "phone"
        };

    public const string DefaultSort = "name";

    // Clamped so a client cannot ask for the entire table in one page.
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 200);
    public int PageNumber { get; init; } = Math.Max(PageNumber, 1);

    public int Skip => (PageNumber - 1) * PageSize;

    /// <summary>The requested sort key if it is recognised, otherwise the default.</summary>
    public string EffectiveSortBy =>
        SortBy is not null && AllowedSortKeys.Contains(SortBy) ? SortBy : DefaultSort;
}
