using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public interface ICookieService
    {
        void SetRefreshTokenCookie(string refreshToken, DateTime expiresAt);
        string? GetRefreshTokenCookie();
        void ClearRefreshTokenCookie();
    }
}
