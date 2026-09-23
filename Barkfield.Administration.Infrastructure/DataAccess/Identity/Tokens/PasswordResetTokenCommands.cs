using Barkfield.Administration.Application.DataAccess.Identity.Tokens;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Identity.Tokens;

public class PasswordResetTokenCommands : IPasswordResetTokenCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public PasswordResetTokenCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(PasswordResetTokenDto token, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(token);

        const string sql = @"
            INSERT INTO dbo.PasswordResetTokens (Id, UserId, TokenHash, ExpiresAt, IsUsed, CreatedAt)
            VALUES (@Id, @UserId, @TokenHash, @ExpiresAt, 0, @CreatedAt);";

        await _sqlExecutor.ExecuteAsync(sql, token, cancellationToken);
    }

    public async Task MarkUsedAsync(Guid tokenId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.PasswordResetTokens
               SET IsUsed = 1,
                   UsedAt = SYSUTCDATETIME()
             WHERE Id = @TokenId AND IsUsed = 0;";

        await _sqlExecutor.ExecuteAsync(sql, new { TokenId = tokenId }, cancellationToken);
    }

    public async Task InvalidateForUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.PasswordResetTokens
               SET IsUsed = 1,
                   UsedAt = SYSUTCDATETIME()
             WHERE UserId = @UserId AND IsUsed = 0;";

        await _sqlExecutor.ExecuteAsync(sql, new { UserId = userId }, cancellationToken);
    }
}
