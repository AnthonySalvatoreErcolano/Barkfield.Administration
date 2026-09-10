using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Barkfield.Administration.Infrastructure.Services.Identity
{
    internal class TokenGenerator(IOptions<JwtSettings> options): ITokenGenerator
    {
        public readonly JwtSettings _jwtOptions = options.Value;
        public string GenerateJwtToken(Guid userId, string email)
        {
            var tokenHandler = new JwtSecurityTokenHandler();
            var key = Encoding.UTF8.GetBytes(_jwtOptions.Secret);

            var tokenDescriptor = new SecurityTokenDescriptor
            {
                Subject = new ClaimsIdentity([
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email)
                ]),
                Expires = DateTime.UtcNow.AddMinutes(_jwtOptions.ExpiryMinutes),
                SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(key), SecurityAlgorithms.HmacSha256Signature),
                Issuer = _jwtOptions.Issuer,
                Audience = _jwtOptions.Audience,
            };

            var token = tokenHandler.CreateToken(tokenDescriptor);
            return tokenHandler.WriteToken(token);
        }

        public  string GenerateRefreshTokenString()
        {
            return GenerateRawToken();
        }

        //public  string HashToken(string token)
        //{
        //    using var sha256 = SHA256.Create();
        //    var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
        //    return Convert.ToBase64String(bytes);
        //}

        public string GenerateRawToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        public string HashToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
                throw new ArgumentException("Token cannot be null or empty.", nameof(token));

            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes);
        }

        public bool VerifyToken(string rawToken, string storedTokenHash)
        {
            if (string.IsNullOrWhiteSpace(rawToken) || string.IsNullOrWhiteSpace(storedTokenHash))
                return false;

            string computedHash = HashToken(rawToken);

            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(computedHash),
                Encoding.UTF8.GetBytes(storedTokenHash));
        }
    }
}
