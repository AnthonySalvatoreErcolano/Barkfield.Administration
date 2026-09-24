using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Subscriptions;

public class SubscriptionQueries : ISubscriptionQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// Maps an accepted sort key to a fixed ORDER BY expression.
    /// </summary>
    /// <remarks>
    /// The client's key is never concatenated into SQL — it is looked up here, because
    /// parameters cannot stand in for column names and a free-text ORDER BY would be an
    /// injection hole.
    /// </remarks>
    private static readonly Dictionary<string, string[]> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["nextDelivery"] = ["s.NextDeliveryDate"],
        ["customer"] = ["c.LastName", "c.FirstName"],
        ["status"] = ["s.Status"],
        ["createdAt"] = ["s.CreatedAt"],
        ["lastDelivery"] = ["s.LastDeliveryDate"],
        ["name"] = ["s.Name"]
    };

    /// <summary>
    /// Builds the ORDER BY, applying the direction to every column and appending Id.
    /// </summary>
    /// <remarks>
    /// The direction has to repeat per column — "LastName, FirstName DESC" sorts only FirstName
    /// descending. Id is the tiebreaker so paging is stable: many subscriptions share a next
    /// delivery date, and without it those rows can appear on two pages or none.
    /// </remarks>
    private static string BuildOrderBy(string sortKey, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";
        var columns = SortColumns[sortKey].Select(column => $"{column} {direction}");

        return string.Join(", ", columns) + ", s.Id";
    }

    /// <summary>Shared by the paged list and a customer's list so the two cannot drift.</summary>
    private const string ListColumns = @"
        s.Id,
        s.CustomerId,
        s.Name,
        c.FirstName + ' ' + c.LastName AS CustomerName,
        s.Status,
        s.FrequencyInterval,
        s.FrequencyUnit,
        s.FulfillmentMethod,
        s.NextDeliveryDate,
        s.LastDeliveryDate,
        s.SignUpDate,
        s.PausedUntil,
        s.CreatedAt,
        s.UpdatedAt,
        s.Revision,
        (SELECT COUNT(1) FROM dbo.SubscriptionItems si WHERE si.SubscriptionId = s.Id) AS ItemCount,
        (SELECT COUNT(1) FROM dbo.RotationGroups rg WHERE rg.SubscriptionId = s.Id) AS RotationGroupCount,
        (SELECT COUNT(1) FROM dbo.SubscriptionAddOns sa
          WHERE sa.SubscriptionId = s.Id AND sa.ConsumedAt IS NULL) AS PendingAddOnCount,
        (SELECT COUNT(DISTINCT p.Id)
           FROM dbo.Products p
          WHERE p.IsActive = 0
            AND (EXISTS (SELECT 1 FROM dbo.SubscriptionItems si
                          WHERE si.SubscriptionId = s.Id AND si.ProductId = p.Id)
              OR EXISTS (SELECT 1 FROM dbo.RotationGroupItems rgi
                         INNER JOIN dbo.RotationGroups rg2 ON rg2.Id = rgi.RotationGroupId
                          WHERE rg2.SubscriptionId = s.Id AND rgi.ProductId = p.Id)
              OR EXISTS (SELECT 1 FROM dbo.SubscriptionAddOns sa2
                          WHERE sa2.SubscriptionId = s.Id AND sa2.ConsumedAt IS NULL
                            AND sa2.ProductId = p.Id))) AS InactiveProductCount";

    public SubscriptionQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PagedResult<SubscriptionListItemDto>> SearchAsync(
        SubscriptionFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var where = new StringBuilder(@"
            FROM dbo.Subscriptions s
            INNER JOIN dbo.Customers c ON c.Id = s.CustomerId
            WHERE 1 = 1 ");

        var parameters = new DynamicParameters();

        // Cancellation is permanent, so canceled subscriptions are history rather than a
        // working list. An explicit status filter overrides this.
        if (!filter.IncludeCanceled && filter.Status is null)
        {
            where.Append(" AND s.Status <> 4 ");
        }

        if (filter.Status is not null)
        {
            where.Append(" AND s.Status = @Status ");
            parameters.Add("Status", (byte)filter.Status.Value);
        }

        if (filter.CustomerId is not null)
        {
            where.Append(" AND s.CustomerId = @CustomerId ");
            parameters.Add("CustomerId", filter.CustomerId.Value);
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            where.Append(@" AND (s.Name LIKE @SearchTerm
                             OR c.FirstName LIKE @SearchTerm
                             OR c.LastName LIKE @SearchTerm
                             OR c.Email LIKE @SearchTerm) ");

            // Escape LIKE wildcards so a literal % or _ in the search box does not match everything.
            string term = filter.SearchTerm.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            parameters.Add("SearchTerm", $"%{term}%");
        }

        if (filter.DueFrom is not null)
        {
            where.Append(" AND s.NextDeliveryDate >= @DueFrom ");
            parameters.Add("DueFrom", filter.DueFrom.Value.Date);
        }

        if (filter.DueTo is not null)
        {
            where.Append(" AND s.NextDeliveryDate <= @DueTo ");
            parameters.Add("DueTo", filter.DueTo.Value.Date);
        }

        // The "due back" worklist. Nothing resumes a pause automatically, so this is how staff
        // find the subscriptions that have quietly run past their return date.
        if (filter.PauseExpired == true)
        {
            where.Append(" AND s.Status = 3 AND s.PausedUntil IS NOT NULL AND s.PausedUntil <= @Today ");
            parameters.Add("Today", DateTime.UtcNow.Date);
        }

        string orderBy = BuildOrderBy(filter.EffectiveSortBy, filter.SortDescending);

        parameters.Add("Skip", filter.Skip);
        parameters.Add("PageSize", filter.PageSize);

        // Count and page in one round trip.
        string sql = $@"
            SELECT COUNT(1) {where};

            SELECT {ListColumns}
            {where}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        int totalCount = await reader.ReadSingleAsync<int>();
        var items = await reader.ReadAsync<SubscriptionListItemDto>();

        return new PagedResult<SubscriptionListItemDto>(
            items.ToList(),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    public async Task<IReadOnlyCollection<SubscriptionListItemDto>> GetByCustomerIdAsync(
        Guid customerId,
        bool includeCanceled = false,
        CancellationToken cancellationToken = default)
    {
        string predicate = includeCanceled ? "1 = 1" : "s.Status <> 4";

        string sql = $@"
            SELECT {ListColumns}
            FROM dbo.Subscriptions s
            INNER JOIN dbo.Customers c ON c.Id = s.CustomerId
            WHERE s.CustomerId = @CustomerId AND {predicate}
            ORDER BY s.NextDeliveryDate, s.Id;";

        var subscriptions = await _sqlExecutor.QueryAsync<SubscriptionListItemDto>(
            sql, new { CustomerId = customerId }, cancellationToken);

        return subscriptions.ToList();
    }

    /// <summary>
    /// Loads the whole aggregate in one round trip: the subscription, its static items, its
    /// rotation groups, those groups' items, and its pending add-ons.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Five result sets rather than five queries. This read also feeds the write path, so a
    /// mutation pays for exactly one trip to the database before its save.
    /// </para>
    /// <para>
    /// Consumed add-ons are excluded. They are delivery history — keeping them here would make
    /// every rehydration carry every add-on the customer has ever had, and the delivery log is
    /// where that belongs.
    /// </para>
    /// </remarks>
    public async Task<SubscriptionDetailDto?> GetByIdAsync(
        Guid subscriptionId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                s.Id, s.CustomerId, s.Name,
                c.FirstName + ' ' + c.LastName AS CustomerName,
                s.Status, s.FrequencyInterval, s.FrequencyUnit, s.FulfillmentMethod,
                s.NextDeliveryDate, s.LastDeliveryDate, s.SignUpDate, s.PausedUntil,
                s.CreatedAt, s.UpdatedAt, s.Revision
            FROM dbo.Subscriptions s
            INNER JOIN dbo.Customers c ON c.Id = s.CustomerId
            WHERE s.Id = @SubscriptionId;

            SELECT
                si.Id, si.ProductId, p.Name AS ProductName, p.Price AS UnitPrice,
                p.IsActive AS ProductIsActive, si.Quantity, si.CreatedAt, si.UpdatedAt
            FROM dbo.SubscriptionItems si
            INNER JOIN dbo.Products p ON p.Id = si.ProductId
            WHERE si.SubscriptionId = @SubscriptionId
            ORDER BY si.CreatedAt, si.Id;

            SELECT rg.Id, rg.Name, rg.RotationPosition, rg.IsActive, rg.CreatedAt, rg.UpdatedAt
            FROM dbo.RotationGroups rg
            WHERE rg.SubscriptionId = @SubscriptionId
            ORDER BY rg.CreatedAt, rg.Id;

            SELECT
                rgi.Id, rgi.RotationGroupId, rgi.ProductId, p.Name AS ProductName,
                p.Price AS UnitPrice, p.IsActive AS ProductIsActive,
                rgi.SequenceOrder, rgi.Quantity, rgi.CreatedAt, rgi.UpdatedAt
            FROM dbo.RotationGroupItems rgi
            INNER JOIN dbo.RotationGroups rg ON rg.Id = rgi.RotationGroupId
            INNER JOIN dbo.Products p ON p.Id = rgi.ProductId
            WHERE rg.SubscriptionId = @SubscriptionId
            ORDER BY rgi.SequenceOrder, rgi.Id;

            SELECT
                sa.Id, sa.ProductId, p.Name AS ProductName, p.Price AS UnitPrice,
                p.IsActive AS ProductIsActive, sa.Quantity, sa.Note,
                sa.CreatedAt, NULL AS UpdatedAt, sa.ConsumedAt
            FROM dbo.SubscriptionAddOns sa
            INNER JOIN dbo.Products p ON p.Id = sa.ProductId
            WHERE sa.SubscriptionId = @SubscriptionId AND sa.ConsumedAt IS NULL
            ORDER BY sa.CreatedAt, sa.Id;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(
            sql, new { SubscriptionId = subscriptionId }, cancellationToken);

        var subscription = await reader.ReadSingleOrDefaultAsync<SubscriptionDetailDto>();
        if (subscription is null) return null;

        subscription.Items = (await reader.ReadAsync<SubscriptionLineDto>()).ToList();

        var groups = (await reader.ReadAsync<RotationGroupDto>()).ToList();
        var groupItems = await reader.ReadAsync<RotationGroupItemDto>();

        // Stitched in memory rather than one query per group. SequenceOrder from the ORDER BY
        // is preserved, which matters: the rotation cursor indexes into this list.
        var itemsByGroup = groupItems
            .GroupBy(i => i.RotationGroupId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<RotationGroupItemDto>)g.ToList());

        foreach (RotationGroupDto group in groups)
        {
            if (itemsByGroup.TryGetValue(group.Id, out IReadOnlyList<RotationGroupItemDto>? items))
            {
                group.Items = items;
            }
        }

        subscription.RotationGroups = groups;
        subscription.PendingAddOns = (await reader.ReadAsync<SubscriptionAddOnDto>()).ToList();

        return subscription;
    }
}
