using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Connections.Database;
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
    internal class IdentityService(PasswordHasher passwordHasher, IOptions<JwtSettings> options, ISqlExecutor sqlExecutor) : IIdentityService
    {
        private readonly JwtSettings _jwtOptions = options.Value;

        public async Task<AuthenticationResult?> LoginAsync(string email,string password)
        {


            LoginDto? user = await sqlExecutor.QuerySingleAsync<LoginDto>(@"SELECT Id, Name, Email, PasswordHash, IsActive 
            FROM Users 
            WHERE Email = @Email AND IsActive=1;", new { Email = email });

            if (user == null) return null;

            var verificationResult = passwordHasher.VerifyPassword( password,user.PasswordHash);

            if (!verificationResult) return null;

            var accessToken = GenerateJwtToken(user.Id, email);
            var rawRefreshToken = GenerateRefreshTokenString();
            var refreshTokenHash = HashToken(rawRefreshToken);

            var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_jwtOptions.RefreshTokenExpiryDays > 0 ? _jwtOptions.RefreshTokenExpiryDays : 7);
            
            const string insertRefreshTokenSql = @"
                INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt)
                VALUES (@UserId, @TokenHash, @ExpiresAt, @CreatedAt);";
            await sqlExecutor.ExecuteAsync(insertRefreshTokenSql, new
            {
                UserId = user.Id,
                TokenHash = refreshTokenHash,
                ExpiresAt = refreshTokenExpiresAt,
                CreatedAt = DateTime.UtcNow
            });
            return new AuthenticationResult(
                accessToken,
                rawRefreshToken,
                refreshTokenExpiresAt,
                email,
                user.Id);
        }

        public async Task<Guid?> RegisterAsync(string email, string password, string name)
        {
            string hashedPassword = passwordHasher.HashPassword(password);
            // Dapper execution here...

            return Guid.NewGuid();
        }


        //Return here
        public async Task LogoutAsync(Guid userId, string? refreshToken, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var tokenHash = HashToken(refreshToken);

                const string revokeTokenSql = @"
            UPDATE RefreshTokens
            SET RevokedAt = GETUTCDATE(),
                ReasonRevoked = 'User Logged Out'
            WHERE TokenHash = @TokenHash 
              AND UserId = @UserId 
              AND RevokedAt IS NULL;";

                await sqlExecutor.ExecuteAsync(revokeTokenSql, new
                {
                    TokenHash = tokenHash,
                    UserId = userId
                });

                return;
            }

            const string revokeAllTokensSql = @"
        UPDATE RefreshTokens
        SET RevokedAt = GETUTCDATE(),
            ReasonRevoked = 'User Global Logout'
        WHERE UserId = @UserId 
          AND RevokedAt IS NULL;";

            await sqlExecutor.ExecuteAsync(revokeAllTokensSql, new { UserId = userId });
        }


        private string GenerateJwtToken(Guid userId, string email)
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

        private static string GenerateRefreshTokenString()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            return Convert.ToBase64String(randomNumber);
        }

        private static string HashToken(string token)
        {
            using var sha256 = SHA256.Create();
            var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(token));
            return Convert.ToBase64String(bytes);
        }
    }
}
