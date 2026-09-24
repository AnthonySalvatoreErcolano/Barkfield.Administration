using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Subscriptions;

/// <summary>
/// Search, sort and pagination for the subscription dashboard.
/// </summary>
/// <param name="SearchTerm">Matched against the subscription name and the customer's name and email.</param>
/// <param name="CustomerId">Narrows to one customer. A customer may hold several subscriptions.</param>
/// <param name="Status">Exact lifecycle state. Omit for everything but canceled.</param>
/// <param name="IncludeCanceled">
/// Canceled subscriptions are excluded by default. Cancellation is permanent, so they are
/// history rather than a working list.
/// </param>
/// <param name="DueFrom">Only subscriptions whose next delivery is on or after this date.</param>
/// <param name="DueTo">Only subscriptions whose next delivery is on or before this date.</param>
/// <param name="PauseExpired">
/// True returns paused subscriptions whose end date has passed — the "due back" worklist.
/// Nothing resumes them automatically, so this is how staff find them.
/// </param>
public record SubscriptionFilter(
    string? SearchTerm = null,
    Guid? CustomerId = null,
    SubscriptionStatus? Status = null,
    bool IncludeCanceled = false,
    DateTime? DueFrom = null,
    DateTime? DueTo = null,
    bool? PauseExpired = null,
    int PageNumber = 1,
    int PageSize = 25,
    string? SortBy = null,
    bool SortDescending = false)
{
    /// <summary>
    /// Sort keys the API accepts. Anything else falls back to <see cref="DefaultSort"/>.
    /// </summary>
    /// <remarks>
    /// The key never reaches SQL as text — the data layer maps it to a fixed column expression,
    /// because a free-text ORDER BY would be an injection hole.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedSortKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "nextDelivery", "customer", "status", "createdAt", "lastDelivery", "name"
        };

    /// <summary>
    /// Next delivery date ascending. The dashboard's job is "what is coming up", so the
    /// soonest delivery belongs at the top.
    /// </summary>
    public const string DefaultSort = "nextDelivery";

    // Clamped so a client cannot ask for the entire table in one page.
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 200);
    public int PageNumber { get; init; } = Math.Max(PageNumber, 1);

    public int Skip => (PageNumber - 1) * PageSize;

    public string EffectiveSortBy =>
        SortBy is not null && AllowedSortKeys.Contains(SortBy) ? SortBy : DefaultSort;
}
