using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public interface ITokenGenerator
    {
        public string GenerateJwtToken(Guid userId, string email);
        public string GenerateRefreshTokenString();
        public string GenerateRawToken();
        public string HashToken(string token);
        public bool VerifyToken(string rawToken, string storedTokenHash);
    }
}
