using Barkfield.Administration.Application.DataAccess.Identity.Tokens;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Identity.Tokens;

public class PasswordResetTokenQueries : IPasswordResetTokenQueries
{
    private readonly ISqlExecutor _sqlExecutor;

    public PasswordResetTokenQueries(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task<PasswordResetTokenDto?> GetActiveForUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT TOP 1 Id, UserId, TokenHash, ExpiresAt, IsUsed, CreatedAt, UsedAt
            FROM dbo.PasswordResetTokens
            WHERE UserId = @UserId
              AND IsUsed = 0
              AND ExpiresAt > SYSUTCDATETIME()
            ORDER BY CreatedAt DESC;";

        return await _sqlExecutor.QuerySingleAsync<PasswordResetTokenDto>(
            sql, new { UserId = userId }, cancellationToken);
    }
}
