using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Allergies;

public class AllergyQueries : IAllergyQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    public AllergyQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<IReadOnlyCollection<AllergyDto>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT Id, AllergyName FROM dbo.Allergies ORDER BY AllergyName;";

        var allergies = await _sqlExecutor.QueryAsync<AllergyDto>(sql, null, cancellationToken);

        return allergies.ToList();
    }

    public async Task<IReadOnlyCollection<Guid>> GetExistingIdsAsync(
        IEnumerable<Guid> allergyIds,
        CancellationToken cancellationToken = default)
    {
        var ids = allergyIds?.Distinct().ToList() ?? [];
        if (ids.Count == 0) return [];

        const string sql = "SELECT Id FROM dbo.Allergies WHERE Id IN @AllergyIds;";

        var found = await _sqlExecutor.QueryAsync<Guid>(sql, new { AllergyIds = ids }, cancellationToken);

        return found.ToList();
    }
}
