using Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.RefreshTokens
{
    public class RefreshTokenCommands:IRefreshTokenCommands
    {
        private ISqlExecutor _sqlExecutor;

        public RefreshTokenCommands(ISqlExecutor sqlExecutor)
        {
            _sqlExecutor = sqlExecutor;
        }

        public async Task CreateAsync(RefreshToken token, CancellationToken cancellationToken)
        {
            const string sql = @"
            INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt, CreatedByIp)
            VALUES (@UserId, @TokenHash, @ExpiresAt, @CreatedAt, @CreatedByIp);";

            var command = new CommandDefinition(sql, token, cancellationToken: cancellationToken);
            await _sqlExecutor.ExecuteAsync(sql,token);
        }

        public async Task RevokeTokenAsync(string tokenHash,string reason,string? revokedByIp,string? replacedByTokenHash,
        CancellationToken cancellationToken)
        {
            const string sql = @"
            UPDATE RefreshTokens
            SET RevokedAt = GETUTCDATE(),
                ReasonRevoked = @Reason,
                RevokedByIp = @RevokedByIp,
                ReplacedByTokenHash = @ReplacedByTokenHash
            WHERE TokenHash = @TokenHash AND RevokedAt IS NULL;";

            var command = new CommandDefinition(sql, new
            {
                TokenHash = tokenHash,
                Reason = reason,
                RevokedByIp = revokedByIp,
                ReplacedByTokenHash = replacedByTokenHash
            }, cancellationToken: cancellationToken);

            await _sqlExecutor.ExecuteAsync(sql, new
            {
                TokenHash = tokenHash,
                Reason = reason,
                RevokedByIp = revokedByIp,
                ReplacedByTokenHash = replacedByTokenHash
            });
        }

        public async Task RevokeAllUserTokensAsync(string userId,string reason,string? revokedByIp, CancellationToken cancellationToken)
        {
            const string sql = @"
            UPDATE RefreshTokens
            SET RevokedAt = GETUTCDATE(),
                ReasonRevoked = @Reason,
                RevokedByIp = @RevokedByIp
            WHERE UserId = @UserId AND RevokedAt IS NULL;";

            var command = new CommandDefinition(sql, new
            {
                UserId = userId,
                Reason = reason,
                RevokedByIp = revokedByIp
            }, cancellationToken: cancellationToken);

            await _sqlExecutor.ExecuteAsync(sql, new
            {
                UserId = userId,
                Reason = reason,
                RevokedByIp = revokedByIp
            });
        }

        public async Task DeleteExpiredTokensAsync(CancellationToken cancellationToken)
        {
            const string sql = @"
            DELETE FROM RefreshTokens
            WHERE ExpiresAt <= GETUTCDATE() OR RevokedAt IS NOT NULL;";

            var command = new CommandDefinition(sql, cancellationToken: cancellationToken);
            await _sqlExecutor.ExecuteAsync(sql);
        }
    }
}
