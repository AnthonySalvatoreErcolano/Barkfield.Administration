using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Customers;

public class CustomerQueries : ICustomerQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// Maps an accepted sort key to a fixed ORDER BY expression.
    /// </summary>
    /// <remarks>
    /// The client's sort key is never concatenated into SQL. It is looked up here, and an
    /// unrecognised key falls back to the default — a free-text ORDER BY would be an
    /// injection hole, and parameters cannot be used for column names.
    /// </remarks>
    private static readonly Dictionary<string, string[]> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["c.LastName", "c.FirstName"],
        ["email"] = ["c.Email"],
        ["createdAt"] = ["c.CreatedAt"],
        ["city"] = ["c.City"],
        ["phone"] = ["c.PhoneNumber"]
    };

    /// <summary>
    /// Builds the ORDER BY clause, applying the direction to every column.
    /// </summary>
    /// <remarks>
    /// The direction has to be repeated per column: "LastName, FirstName DESC" sorts only
    /// FirstName descending, which silently makes ascending and descending identical for
    /// any multi-column key.
    ///
    /// Id is appended as a tiebreaker so paging is stable. Without it, rows sharing a sort
    /// value have no guaranteed order and can appear on two pages or none.
    /// </remarks>
    private static string BuildOrderBy(string sortKey, bool descending)
    {
        string direction = descending ? "DESC" : "ASC";
        var columns = SortColumns[sortKey].Select(column => $"{column} {direction}");

        return string.Join(", ", columns) + ", c.Id";
    }

    public CustomerQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PagedResult<CustomerListItemDto>> SearchAsync(
        CustomerFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var where = new StringBuilder(" FROM dbo.Customers c WHERE 1 = 1 ");
        var parameters = new DynamicParameters();

        if (!filter.IncludeInactive)
        {
            where.Append(" AND c.IsActive = 1 ");
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            where.Append(@" AND (c.FirstName LIKE @SearchTerm
                             OR c.LastName LIKE @SearchTerm
                             OR c.Email LIKE @SearchTerm
                             OR c.PhoneNumber LIKE @SearchTerm) ");

            // Escape LIKE wildcards so a literal % or _ in a search box does not match everything.
            string term = filter.SearchTerm.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            parameters.Add("SearchTerm", $"%{term}%");
        }

        if (!string.IsNullOrWhiteSpace(filter.Email))
        {
            where.Append(" AND c.Email = @Email ");
            parameters.Add("Email", filter.Email.Trim().ToLowerInvariant());
        }

        if (filter.HasSquareAccount.HasValue)
        {
            where.Append(filter.HasSquareAccount.Value
                ? " AND c.SquareCustomerId IS NOT NULL "
                : " AND c.SquareCustomerId IS NULL ");
        }

        string orderBy = BuildOrderBy(filter.EffectiveSortBy, filter.SortDescending);

        parameters.Add("Skip", filter.Skip);
        parameters.Add("PageSize", filter.PageSize);

        // Count and page in one round trip.
        string sql = $@"
            SELECT COUNT(1) {where};

            SELECT
                c.Id,
                c.FirstName,
                c.LastName,
                c.Email,
                c.PhoneNumber,
                c.City,
                c.SquareCustomerId,
                c.IsActive,
                c.CreatedAt,
                (SELECT COUNT(1) FROM dbo.Pets p WHERE p.CustomerId = c.Id) AS PetCount,
                (SELECT COUNT(1) FROM dbo.Subscriptions s
                  WHERE s.CustomerId = c.Id AND s.Status IN (1, 2)) AS ActiveSubscriptionCount
            {where}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        int totalCount = await reader.ReadSingleAsync<int>();
        var items = await reader.ReadAsync<CustomerListItemDto>();

        return new PagedResult<CustomerListItemDto>(
            items.ToList(),
            totalCount,
            filter.PageNumber,
            filter.PageSize);
    }

    public Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        GetDetailAsync("c.Id = @Id", new { Id = id }, cancellationToken);

    public Task<CustomerDetailDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<CustomerDetailDto?>(null);

        return GetDetailAsync("c.Email = @Email", new { Email = email.Trim().ToLowerInvariant() }, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        Guid? excludingCustomerId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Customers
                 WHERE Email = @Email
                   AND (@ExcludingId IS NULL OR Id <> @ExcludingId)
            ) THEN 1 ELSE 0 END;";

        return await _sqlExecutor.QuerySingleAsync<bool>(
            sql,
            new { Email = email.Trim().ToLowerInvariant(), ExcludingId = excludingCustomerId },
            cancellationToken);
    }

    /// <summary>
    /// Loads one customer and their pets in a single round trip.
    /// </summary>
    private async Task<CustomerDetailDto?> GetDetailAsync(
        string predicate,
        object parameters,
        CancellationToken cancellationToken)
    {
        string sql = $@"
            SELECT
                c.Id, c.SquareCustomerId, c.Email, c.FirstName, c.LastName,
                c.PhoneNumber, c.Notes,
                c.Street, c.City, c.State, c.ZipCode,
                c.AccessNotes, c.ServiceDurationMinutes,
                c.PreferredWindowStart, c.PreferredWindowEnd,
                c.Latitude, c.Longitude,
                c.IsActive, c.CreatedAt, c.UpdatedAt
            FROM dbo.Customers c
            WHERE {predicate};

            SELECT
                p.Id, p.CustomerId, p.Name, p.Birthday, p.PetType,
                p.Breed, p.Notes, p.PictureUrl, p.IsActive, p.CreatedAt, p.UpdatedAt
            FROM dbo.Pets p
            INNER JOIN dbo.Customers c ON c.Id = p.CustomerId
            WHERE {predicate} AND p.IsActive = 1
            ORDER BY p.Name;

            SELECT pa.PetId, a.Id, a.AllergyName
            FROM dbo.PetAllergies pa
            INNER JOIN dbo.Allergies a ON a.Id = pa.AllergyId
            INNER JOIN dbo.Pets p ON p.Id = pa.PetId
            INNER JOIN dbo.Customers c ON c.Id = p.CustomerId
            WHERE {predicate} AND p.IsActive = 1
            ORDER BY a.AllergyName;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        var customer = await reader.ReadSingleOrDefaultAsync<CustomerDetailDto>();
        if (customer is null) return null;

        var pets = (await reader.ReadAsync<PetDto>()).ToList();
        var allergyRows = await reader.ReadAsync<CustomerPetAllergyRow>();

        // Allergies come back in the same round trip and are stitched on here — staff check
        // them before a protein goes in a box, so a pet without them is not much use.
        var byPet = allergyRows
            .GroupBy(r => r.PetId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyCollection<AllergyDto>)g
                    .Select(r => new AllergyDto { Id = r.Id, AllergyName = r.AllergyName })
                    .ToList());

        foreach (var pet in pets)
        {
            if (byPet.TryGetValue(pet.Id, out var allergies))
            {
                pet.Allergies = allergies;
            }
        }

        customer.Pets = pets;

        return customer;
    }

    private sealed class CustomerPetAllergyRow
    {
        public Guid PetId { get; set; }
        public Guid Id { get; set; }
        public string AllergyName { get; set; } = string.Empty;
    }
}
