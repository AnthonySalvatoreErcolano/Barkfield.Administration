using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Allergies;

public class AllergyCommands : IAllergyCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public AllergyCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    /// <summary>
    /// Adds an allergy if that name is new, otherwise returns the existing id.
    /// </summary>
    /// <remarks>
    /// Written as a single statement so two staff adding the same allergy at once cannot
    /// create a duplicate. The unique index on AllergyName is the real guard; the SELECT
    /// afterwards returns whichever row won.
    /// </remarks>
    public async Task<Guid> GetOrCreateAsync(string allergyName, CancellationToken cancellationToken = default)
    {
        string name = allergyName?.Trim() ?? string.Empty;

        const string sql = @"
            IF NOT EXISTS (SELECT 1 FROM dbo.Allergies WHERE AllergyName = @Name)
            BEGIN
                INSERT INTO dbo.Allergies (Id, AllergyName) VALUES (NEWID(), @Name);
            END

            SELECT Id FROM dbo.Allergies WHERE AllergyName = @Name;";

        return await _sqlExecutor.QuerySingleAsync<Guid>(sql, new { Name = name }, cancellationToken);
    }
}
