using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Identity.Roles;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Users;

public class UserQueries : IUserQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    /// <summary>
    /// Maps an accepted sort key to its columns. The client's key is looked up here and
    /// never concatenated into SQL; column names cannot be parameterised.
    /// </summary>
    private static readonly Dictionary<string, string[]> SortColumns = new(StringComparer.OrdinalIgnoreCase)
    {
        ["name"] = ["u.Name"],
        ["email"] = ["u.Email"],
        ["createdAt"] = ["u.CreatedAt"]
    };

    public UserQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PagedResult<UserListItemDto>> SearchAsync(
        UserFilter filter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);

        var where = new StringBuilder(" FROM dbo.Users u WHERE 1 = 1 ");
        var parameters = new DynamicParameters();

        if (!filter.IncludeInactive)
        {
            where.Append(" AND u.IsActive = 1 ");
        }

        if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
        {
            where.Append(" AND (u.Name LIKE @SearchTerm OR u.Email LIKE @SearchTerm) ");

            // Escape LIKE wildcards so a literal % in the search box does not match everyone.
            string term = filter.SearchTerm.Trim()
                .Replace("[", "[[]")
                .Replace("%", "[%]")
                .Replace("_", "[_]");

            parameters.Add("SearchTerm", $"%{term}%");
        }

        if (filter.RoleId.HasValue)
        {
            where.Append(@" AND EXISTS (SELECT 1 FROM dbo.UserRoles ur
                                         WHERE ur.UserId = u.Id AND ur.RoleId = @RoleId) ");
            parameters.Add("RoleId", filter.RoleId.Value);
        }

        string direction = filter.SortDescending ? "DESC" : "ASC";
        string orderBy = string.Join(", ", SortColumns[filter.EffectiveSortBy].Select(c => $"{c} {direction}")) + ", u.Id";

        parameters.Add("Skip", filter.Skip);
        parameters.Add("PageSize", filter.PageSize);

        string sql = $@"
            SELECT COUNT(1) {where};

            SELECT
                u.Id, u.Name, u.Email, u.IsActive, u.IsAdmin, u.CreatedAt,
                ISNULL(STUFF((
                    SELECT ', ' + r.Name
                    FROM dbo.UserRoles ur
                    INNER JOIN dbo.Roles r ON r.Id = ur.RoleId
                    WHERE ur.UserId = u.Id
                    ORDER BY r.Name
                    FOR XML PATH(''), TYPE).value('.', 'NVARCHAR(MAX)'), 1, 2, ''), '') AS Roles
            {where}
            ORDER BY {orderBy}
            OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        int totalCount = await reader.ReadSingleAsync<int>();
        var items = await reader.ReadAsync<UserListItemDto>();

        return new PagedResult<UserListItemDto>(items.ToList(), totalCount, filter.PageNumber, filter.PageSize);
    }

    public Task<UserDetailDto?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
        GetDetailAsync("u.Id = @Id", new { Id = userId }, cancellationToken);

    public Task<UserDetailDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return Task.FromResult<UserDetailDto?>(null);

        return GetDetailAsync("u.Email = @Email", new { Email = Normalize(email) }, cancellationToken);
    }

    public async Task<UserCredentialsDto?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return null;

        const string sql = @"
            SELECT Id, Name, Email, PasswordHash, IsActive, IsAdmin, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Email = @Email;";

        return await _sqlExecutor.QuerySingleAsync<UserCredentialsDto>(
            sql, new { Email = Normalize(email) }, cancellationToken);
    }

    public async Task<UserCredentialsDto?> GetCredentialsByIdAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Name, Email, PasswordHash, IsActive, IsAdmin, CreatedAt, UpdatedAt
            FROM dbo.Users
            WHERE Id = @Id;";

        return await _sqlExecutor.QuerySingleAsync<UserCredentialsDto>(sql, new { Id = userId }, cancellationToken);
    }

    public async Task<bool> EmailExistsAsync(
        string email,
        Guid? excludingUserId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email)) return false;

        const string sql = @"
            SELECT CASE WHEN EXISTS (
                SELECT 1 FROM dbo.Users
                 WHERE Email = @Email
                   AND (@ExcludingId IS NULL OR Id <> @ExcludingId)
            ) THEN 1 ELSE 0 END;";

        return await _sqlExecutor.QuerySingleAsync<bool>(
            sql, new { Email = Normalize(email), ExcludingId = excludingUserId }, cancellationToken);
    }

    public async Task<IReadOnlyCollection<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT RoleId FROM dbo.UserRoles WHERE UserId = @UserId;";

        var ids = await _sqlExecutor.QueryAsync<Guid>(sql, new { UserId = userId }, cancellationToken);

        return ids.ToList();
    }

    /// <summary>
    /// Loads one user and their roles in a single round trip. Never selects the password hash.
    /// </summary>
    private async Task<UserDetailDto?> GetDetailAsync(
        string predicate,
        object parameters,
        CancellationToken cancellationToken)
    {
        string sql = $@"
            SELECT u.Id, u.Name, u.Email, u.IsActive, u.IsAdmin, u.CreatedAt, u.UpdatedAt
            FROM dbo.Users u
            WHERE {predicate};

            SELECT r.Id, r.Name, r.Description, r.IsSystemRole
            FROM dbo.Roles r
            INNER JOIN dbo.UserRoles ur ON ur.RoleId = r.Id
            INNER JOIN dbo.Users u ON u.Id = ur.UserId
            WHERE {predicate}
            ORDER BY r.Name;";

        using var reader = await _sqlExecutor.QueryMultipleAsync(sql, parameters, cancellationToken);

        var user = await reader.ReadSingleOrDefaultAsync<UserDetailDto>();
        if (user is null) return null;

        var roles = await reader.ReadAsync<RoleDto>();
        user.Roles = roles.ToList();

        return user;
    }

    private static string Normalize(string email) => email.Trim().ToLowerInvariant();
}
