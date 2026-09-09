using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.RefreshTokens
{
    public record RefreshTokenDto(int TokenId,
    Guid UserId,
    DateTime ExpiresAt,
    DateTime? RevokedAt,
    string Email,
    bool IsActive)
 
}
