using Barkfield.Administration.Application.DataAccess.Identity.Roles;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Identity.Roles;

public class RoleQueries : IRoleQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    public RoleQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<IReadOnlyCollection<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT Id, Name, Description, IsSystemRole
            FROM dbo.Roles
            ORDER BY Name;";

        var roles = await _sqlExecutor.QueryAsync<RoleDto>(sql, null, cancellationToken);

        return roles.ToList();
    }

    public async Task<IReadOnlyCollection<Guid>> GetExistingIdsAsync(
        IEnumerable<Guid> roleIds,
        CancellationToken cancellationToken = default)
    {
        var ids = roleIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return [];

        // Dapper expands a list parameter into an IN clause.
        const string sql = "SELECT Id FROM dbo.Roles WHERE Id IN @RoleIds;";

        var found = await _sqlExecutor.QueryAsync<Guid>(sql, new { RoleIds = ids }, cancellationToken);

        return found.ToList();
    }
}
