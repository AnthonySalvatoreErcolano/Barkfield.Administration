namespace Barkfield.Administration.Application.DataAccess.Users;

/// <summary>
/// Search, sort and pagination for the staff user list.
/// </summary>
public record UserFilter(
    string? SearchTerm = null,
    Guid? RoleId = null,
    bool IncludeInactive = false,
    int PageNumber = 1,
    int PageSize = 25,
    string? SortBy = null,
    bool SortDescending = false)
{
    /// <summary>
    /// Sort keys the API accepts. The key never reaches SQL as text — the data layer maps
    /// it to a fixed column expression, because column names cannot be parameterised.
    /// </summary>
    public static readonly IReadOnlySet<string> AllowedSortKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name", "email", "createdAt" };

    public const string DefaultSort = "name";

    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 200);
    public int PageNumber { get; init; } = Math.Max(PageNumber, 1);

    public int Skip => (PageNumber - 1) * PageSize;

    public string EffectiveSortBy =>
        SortBy is not null && AllowedSortKeys.Contains(SortBy) ? SortBy : DefaultSort;
}
