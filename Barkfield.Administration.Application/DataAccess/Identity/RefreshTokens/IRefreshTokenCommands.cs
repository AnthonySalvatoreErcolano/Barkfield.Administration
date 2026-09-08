using Barkfield.Administration.Application.Services.Identity.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens
{
    public interface IRefreshTokenCommands
    {
        Task RevokeTokenAsync(string tokenHash, string reason, string? revokedByIp, string? replacedByTokenHash, CancellationToken cancellationToken);
        Task RevokeAllUserTokensAsync(string userId, string reason, string? revokedByIp, CancellationToken cancellationToken);
        Task DeleteExpiredTokensAsync(CancellationToken cancellationToken);
        Task CreateAsync(RefreshToken token, CancellationToken cancellationToken);
    }
}
