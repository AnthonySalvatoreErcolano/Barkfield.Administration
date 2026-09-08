using Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.RefreshTokens
{
    public class RefreshTokenQueries:IRefreshTokenQueries
    {
        private ISqlExecutor _sqlExecutor;

        public RefreshTokenQueries(ISqlExecutor sqlExecutor)
        {
            _sqlExecutor = sqlExecutor;
        }
        public async Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken)
        {
            const string sql = @"
            SELECT Id, UserId, TokenHash, ExpiresAt, CreatedAt, CreatedByIp, 
                   RevokedAt, RevokedByIp, ReplacedByTokenHash, ReasonRevoked
            FROM RefreshTokens
            WHERE TokenHash = @TokenHash;";

            return await _sqlExecutor.QuerySingleAsync<RefreshToken>(sql, new { TokenHash = tokenHash });
        }
    }
}
