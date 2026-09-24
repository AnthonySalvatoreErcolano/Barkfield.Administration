using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Deliveries;

/// <summary>
/// Search, sort and pagination for the delivery worklist and dispatch screen.
/// </summary>
/// <param name="SearchTerm">Matched against the customer's name and email and the subscription name.</param>
/// <param name="ScheduledFrom">Scheduled on or after this date.</param>
/// <param name="ScheduledTo">Scheduled on or before this date.</param>
/// <param name="ProcurementStatus">
/// The packing worklist filter. <c>Blocked</c> is "show me what needs a decision"; <c>Ready</c>
/// is "show me what can be packed".
/// </param>
/// <param name="OneOffOnly">True returns only deliveries with no subscription behind them.</param>
/// <param name="IncludeClosed">
/// Delivered, failed and cancelled deliveries are excluded by default — the working list is what
/// still needs doing. History reads set this.
/// </param>
public record DeliveryFilter(
    string? SearchTerm = null,
    Guid? CustomerId = null,
    Guid? SubscriptionId = null,
    DateTime? ScheduledFrom = null,
    DateTime? ScheduledTo = null,
    DeliveryStatus? Status = null,
    ProcurementStatus? ProcurementStatus = null,
    FulfillmentMethod? FulfillmentMethod = null,
    bool? HasPaid = null,
    bool? OneOffOnly = null,
    bool IncludeClosed = false,
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
    /// because parameters cannot stand in for column names.
    /// </remarks>
    public static readonly IReadOnlySet<string> AllowedSortKeys =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "scheduledFor", "customer", "status", "procurement", "createdAt", "total"
        };

    /// <summary>
    /// Scheduled date, then customer. The screen answers "what is going out, and to whom".
    /// </summary>
    public const string DefaultSort = "scheduledFor";

    // Clamped so a client cannot ask for the entire table in one page.
    public int PageSize { get; init; } = Math.Clamp(PageSize, 1, 200);
    public int PageNumber { get; init; } = Math.Max(PageNumber, 1);

    public int Skip => (PageNumber - 1) * PageSize;

    public string EffectiveSortBy =>
        SortBy is not null && AllowedSortKeys.Contains(SortBy) ? SortBy : DefaultSort;
}
