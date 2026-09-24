using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Deliveries;
using Barkfield.Administration.Application.Services.Deliveries.Models;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Deliveries;

public class DeliveryQueries : IDeliveryQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// Maps an accepted sort key to a fixed ORDER BY expression.
    /// </summary>
    /// <remarks>
    /// The client's key is never concatenated into SQL — it is looked up here, because parameters
    /// cannot stand in for column names.
    /// </remarks>
    private static readonly Dictionary<string, string[]> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["scheduledFor"] = ["d.ScheduledFor"],
        ["customer"] = ["c.LastName", "c.FirstName"],
        ["status"] = ["d.Status"],
        ["procurement"] = ["d.ProcurementStatus"],
        ["createdAt"] = ["d.CreatedAt"],
        ["total"] = ["Total"]
    };

    /// <summary>
    /// Builds the ORDER BY, applying the direction to every column and appending Id.
    /// </summary>
    /// <remarks>
    /// Id is the tiebreaker so paging is stable — a whole day's deliveries share one scheduled
    /// date, and without it those rows could appear on two pages or none.
    /// </remarks>
    private static string BuildOrderBy(string sortKey, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";
        var columns = SortColumns[sortKey].Select(column => $"{column} {direction}");

        return string.Join(", ", columns) + ", d.Id";
    }

    /// <summary>
    /// Per-delivery line rollups. Computed in SQL so the worklist can be sorted and filtered on
    /// them without loading every line.
    /// </summary>
    /// <remarks>
    /// The total mirrors <c>DeliveryLine.LineTotal</c>: nothing for a shorted line, the
    /// substitute's price once swapped. Kept in step with the domain by hand — if that rule
    /// changes, this changes.
    /// </remarks>
    private const string LineRollups = @"
        (SELECT COUNT(1) FROM dbo.DeliveryLines dl WHERE dl.DeliveryId = d.Id) AS LineCount,
        (SELECT COUNT(1) FROM dbo.DeliveryLines dl
          WHERE dl.DeliveryId = d.Id AND dl.OrderStatus NOT IN (4,6,7)) AS UnresolvedLineCount,
        (SELECT COUNT(1) FROM dbo.DeliveryLines dl
          WHERE dl.DeliveryId = d.Id AND dl.OrderStatus = 5) AS BlockedLineCount,
        (SELECT ISNULL(SUM(dl.Quantity), 0) FROM dbo.DeliveryLines dl WHERE dl.DeliveryId = d.Id) AS TotalUnits,
        (SELECT ISNULL(SUM(
                    CASE dl.OrderStatus
                        WHEN 7 THEN 0
                        WHEN 6 THEN ISNULL(dl.SubstitutedWithUnitPrice, dl.UnitPrice) * dl.Quantity
                        ELSE dl.UnitPrice * dl.Quantity
                    END), 0)
           FROM dbo.DeliveryLines dl WHERE dl.DeliveryId = d.Id) AS Total";

    private const string ListColumns = @"
        d.Id,
        d.SubscriptionId,
        s.Name AS SubscriptionName,
        d.CustomerId,
        c.FirstName + ' ' + c.LastName AS CustomerName,
        d.ScheduledFor,
        d.CompletedAt,
        d.Status,
        d.FulfillmentMethod,
        d.ProcurementStatus,
        d.HasPaid,
        d.AstroCompleted,
        d.ExternalOrderId,
        d.SentToRoutingAt,
        d.Notes,
        d.FailureReason,
        d.CreatedAt,
        d.UpdatedAt,
        d.Revision,
        d.DeliveryCity";

    public DeliveryQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PagedResult<DeliveryListItemDto>> SearchAsync(
        DeliveryFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var where = new StringBuilder(@"
            FROM dbo.Deliveries d
            INNER JOIN dbo.Customers c ON c.Id = d.CustomerId
            LEFT JOIN dbo.Subscriptions s ON s.Id = d.SubscriptionId
            WHERE 1 = 1 ");

        var parameters = new DynamicParameters();

        // The working list is what still needs doing. Delivered, failed and cancelled are
        // history, and an explicit status filter overrides this.
        if (!filter.IncludeClosed && filter.Status is null)
        {
            where.Append(" AND d.Status NOT IN (5,6,7) ");
        }

        if (filter.Status is not null)
        {
            where.Append(" AND d.Status = @Status ");
            parameters.Add("Status", (byte)filter.Status.Value);
        }

        if (filter.ProcurementStatus is not null)
        {
            where.Append(" AND d.ProcurementStatus = @ProcurementStatus ");
            parameters.Add("ProcurementStatus", (byte)filter.ProcurementStatus.Value);
        }

        if (filter.FulfillmentMethod is not null)
        {
            where.Append(" AND d.FulfillmentMethod = @FulfillmentMethod ");
            parameters.Add("FulfillmentMethod", (byte)filter.FulfillmentMethod.Value);
        }

        if (filter.CustomerId is not null)
        {
            where.Append(" AND d.CustomerId = @CustomerId ");
            parameters.Add("CustomerId", filter.CustomerId.Value);
        }

        if (filter.SubscriptionId is not null)
        {
            where.Append(" AND d.SubscriptionId = @SubscriptionId ");
            parameters.Add("SubscriptionId", filter.SubscriptionId.Value);
        }

        if (filter.ScheduledFrom is not null)
        {
            where.Append(" AND d.ScheduledFor >= @ScheduledFrom ");
            parameters.Add("ScheduledFrom", filter.ScheduledFrom.Value.Date);
        }

        if (filter.ScheduledTo is not null)
        {
            where.Append(" AND d.ScheduledFor <= @ScheduledTo ");
            parameters.Add("ScheduledTo", filter.ScheduledTo.Value.Date);
        }

        if (filter.HasPaid is not null)
        {
            where.Append(" AND d.HasPaid = @HasPaid ");
            parameters.Add("HasPaid", filter.HasPaid.Value);
        }

        if (filter.OneOffOnly == true)
        {
            where.Append(" AND d.SubscriptionId IS NULL ");
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            where.Append(@" AND (c.FirstName LIKE @SearchTerm
                             OR c.LastName LIKE @SearchTerm
                             OR c.Email LIKE @SearchTerm
                             OR s.Name LIKE @SearchTerm) ");

            // Escape LIKE wildcards so a literal % or _ in the search box does not match everything.
            string term = filter.SearchTerm.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            parameters.Add("SearchTerm", $"%{term}%");
        }

        string orderBy = BuildOrderBy(filter.EffectiveSortBy, filter.SortDescending);

        parameters.Add("Skip", filter.Skip);
        parameters.Add("PageSize", filter.PageSize);

        // Count and page in one round trip.
        string sql = $@"
            SELECT COUNT(1) {where};

            SELECT {ListColumns},
                   {LineRollups}
            {where}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        int totalCount = await reader.ReadSingleAsync<int>();
        var items = await reader.ReadAsync<DeliveryListItemDto>();

        return new PagedResult<DeliveryListItemDto>(
            items.ToList(),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    /// <summary>
    /// Loads a delivery and its lines in one round trip.
    /// </summary>
    /// <remarks>
    /// The address comes from the delivery's own snapshot columns. The phone number, access notes
    /// and stop duration are read live from the customer — a gate code changed this morning
    /// applies to tonight's run, which is the opposite of what the address needs.
    /// </remarks>
    public async Task<DeliveryDetailDto?> GetByIdAsync(
        Guid deliveryId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT
                d.Id, d.SubscriptionId, s.Name AS SubscriptionName,
                d.CustomerId, c.FirstName + ' ' + c.LastName AS CustomerName,
                d.ScheduledFor, d.CompletedAt, d.Status, d.FulfillmentMethod, d.ProcurementStatus,
                d.HasPaid, d.AstroCompleted, d.ExternalOrderId, d.SentToRoutingAt,
                d.DeliveryStreet, d.DeliveryCity, d.DeliveryState, d.DeliveryZipCode,
                d.DeliveryLatitude, d.DeliveryLongitude,
                d.RequestedWindowStart, d.RequestedWindowEnd, d.ServiceDurationMinutesOverride,
                d.Notes, d.FailureReason, d.CreatedAt, d.UpdatedAt, d.Revision,
                c.PhoneNumber AS CustomerPhoneNumber,
                c.AccessNotes,
                c.ServiceDurationMinutes
            FROM dbo.Deliveries d
            INNER JOIN dbo.Customers c ON c.Id = d.CustomerId
            LEFT JOIN dbo.Subscriptions s ON s.Id = d.SubscriptionId
            WHERE d.Id = @DeliveryId;

            SELECT
                dl.Id, dl.ProductId, dl.ProductName, dl.UnitPrice, dl.Quantity,
                dl.Source, dl.SourceId, dl.OrderStatus, dl.QuantityReceived,
                dl.SubstitutedWithProductId, dl.SubstitutedWithProductName, dl.SubstitutedWithUnitPrice,
                dl.StatusNote, dl.StatusUpdatedAt
            FROM dbo.DeliveryLines dl
            WHERE dl.DeliveryId = @DeliveryId
            ORDER BY dl.Source, dl.ProductName, dl.Id;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(
            sql, new { DeliveryId = deliveryId }, cancellationToken);

        var delivery = await reader.ReadSingleOrDefaultAsync<DeliveryDetailDto>();
        if (delivery is null) return null;

        delivery.Lines = (await reader.ReadAsync<DeliveryLineDto>()).ToList();

        return delivery;
    }

    public async Task<IReadOnlyCollection<DeliverySheetDto>> GetSheetAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default)
    {
        // Cancelled deliveries are left out; shorted lines are kept so nothing looks lost off
        // the sheet, and the print marks them struck through.
        const string sql = @"
            SELECT
                d.Id AS DeliveryId,
                d.ScheduledFor,
                d.CustomerId,
                c.FirstName + ' ' + c.LastName AS CustomerName,
                c.PhoneNumber,
                d.DeliveryStreet,
                d.DeliveryCity,
                d.FulfillmentMethod,
                s.Name AS SubscriptionName
            FROM dbo.Deliveries d
            INNER JOIN dbo.Customers c ON c.Id = d.CustomerId
            LEFT JOIN dbo.Subscriptions s ON s.Id = d.SubscriptionId
            WHERE d.ScheduledFor BETWEEN @From AND @To
              AND d.Status <> 7
            ORDER BY d.ScheduledFor, c.LastName, c.FirstName, d.Id;

            SELECT
                dl.DeliveryId,
                dl.ProductName,
                dl.Quantity,
                dl.SubstitutedWithProductName,
                dl.OrderStatus
            FROM dbo.DeliveryLines dl
            INNER JOIN dbo.Deliveries d ON d.Id = dl.DeliveryId
            WHERE d.ScheduledFor BETWEEN @From AND @To
              AND d.Status <> 7
            ORDER BY dl.ProductName, dl.Id;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(
            sql, new { From = from.Date, To = to.Date }, cancellationToken);

        var sheets = (await reader.ReadAsync<DeliverySheetDto>()).ToList();
        if (sheets.Count == 0) return sheets;

        var lineRows = await reader.ReadAsync<SheetLineRow>();

        // Stitched in memory rather than a query per delivery.
        var byDelivery = lineRows
            .GroupBy(r => r.DeliveryId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<DeliverySheetLineDto>)g
                    .Select(r => new DeliverySheetLineDto
                    {
                        ProductName = r.ProductName,
                        Quantity = r.Quantity,
                        SubstitutedWithProductName = r.SubstitutedWithProductName,
                        OrderStatus = r.OrderStatus
                    })
                    .ToList());

        foreach (DeliverySheetDto sheet in sheets)
        {
            if (byDelivery.TryGetValue(sheet.DeliveryId, out IReadOnlyCollection<DeliverySheetLineDto>? lines))
            {
                sheet.Lines = lines;
            }
        }

        return sheets;
    }

    public async Task<IReadOnlyCollection<DueSubscriptionDto>> GetDueSubscriptionsAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default)
    {
        /*  Due OR overdue — NextDeliveryDate <= the date being generated. A day nobody generates
            would otherwise drop those subscriptions into a gap they never come out of.

            Statuses: 1 NewSignUp and 2 Active are due normally. 3 Paused is included only when a
            return date has been set and has arrived, because generation is what brings those
            back. 4 Canceled never generates.

            ScheduledLineCount counts what would actually ship on a recurring basis: static items
            plus active rotations that have something in them. Pending add-ons are excluded — an
            add-on alone is not a reason to create a delivery.

            AlreadyGenerated matches the filtered unique index, cancelled deliveries excluded, so
            a cancelled day can be regenerated. */
        const string sql = @"
            SELECT
                s.Id AS SubscriptionId,
                s.Name AS SubscriptionName,
                s.CustomerId,
                c.FirstName + ' ' + c.LastName AS CustomerName,
                s.NextDeliveryDate,
                s.Status,
                s.FulfillmentMethod,
                s.PausedUntil,
                CAST(CASE WHEN c.Street IS NOT NULL AND LTRIM(RTRIM(c.Street)) <> ''
                          THEN 1 ELSE 0 END AS BIT) AS CustomerHasAddress,
                CAST(CASE WHEN EXISTS (
                        SELECT 1 FROM dbo.Deliveries d
                         WHERE d.SubscriptionId = s.Id
                           AND d.ScheduledFor = @DeliveryDate
                           AND d.Status <> 7)
                     THEN 1 ELSE 0 END AS BIT) AS AlreadyGenerated,
                CAST(CASE WHEN s.NextDeliveryDate < @DeliveryDate THEN 1 ELSE 0 END AS BIT) AS IsOverdue,
                (SELECT COUNT(1) FROM dbo.SubscriptionItems si WHERE si.SubscriptionId = s.Id)
              + (SELECT COUNT(1) FROM dbo.RotationGroups rg
                  WHERE rg.SubscriptionId = s.Id
                    AND rg.IsActive = 1
                    AND EXISTS (SELECT 1 FROM dbo.RotationGroupItems rgi
                                 WHERE rgi.RotationGroupId = rg.Id)) AS ScheduledLineCount
            FROM dbo.Subscriptions s
            INNER JOIN dbo.Customers c ON c.Id = s.CustomerId
            WHERE s.NextDeliveryDate <= @DeliveryDate
              AND c.IsActive = 1
              AND (s.Status IN (1, 2)
                OR (s.Status = 3 AND s.PausedUntil IS NOT NULL AND s.PausedUntil <= @DeliveryDate))
            ORDER BY c.LastName, c.FirstName, s.Id;";

        var due = await _sqlExecutor.QueryAsync<DueSubscriptionDto>(
            sql, new { DeliveryDate = deliveryDate.Date }, cancellationToken);

        return due.ToList();
    }

    /// <summary>Flat shape for the sheet's line query, carrying the owning delivery id.</summary>
    private sealed class SheetLineRow
    {
        public Guid DeliveryId { get; set; }
        public string ProductName { get; set; } = string.Empty;
        public int Quantity { get; set; }
        public string? SubstitutedWithProductName { get; set; }
        public Domain.ValueObjects.LineOrderStatus OrderStatus { get; set; }
    }
}
