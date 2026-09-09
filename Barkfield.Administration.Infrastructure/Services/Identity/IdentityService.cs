using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Barkfield.Administration.Infrastructure.DataAccess.RefreshTokens;
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
    internal class IdentityService(IPasswordHasher passwordHasher, TokenGenerator tokenGenerator, ISqlExecutor sqlExecutor) : IIdentityService
    {
        
        public async Task<AuthenticationResult?> LoginAsync(string email,string password)
        {


            LoginDto? user = await sqlExecutor.QuerySingleAsync<LoginDto>(@"SELECT Id, Name, Email, PasswordHash, IsActive 
            FROM Users 
            WHERE Email = @Email AND IsActive=1;", new { Email = email });

            if (user == null) return null;

            var verificationResult = passwordHasher.VerifyPassword( password,user.PasswordHash);

            if (!verificationResult) return null;

            var accessToken = tokenGenerator.GenerateJwtToken(user.Id, email);
            var rawRefreshToken = tokenGenerator.GenerateRefreshTokenString();
            var refreshTokenHash = tokenGenerator.HashToken(rawRefreshToken);

            var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(tokenGenerator._jwtOptions.RefreshTokenExpiryDays > 0 ? tokenGenerator._jwtOptions.RefreshTokenExpiryDays : 7);
            
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
        public async Task LogoutAsync(string? userId, string? refreshToken, CancellationToken cancellationToken)
        {
            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                var tokenHash = tokenGenerator.HashToken(refreshToken);

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
        public async Task<AuthenticationResult?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
        {
            var tokenHash = tokenGenerator.HashToken(refreshToken);

            // 1. Fetch active token & user details via Dapper
            const string getRefreshTokenSql = @"
        SELECT 
            rt.Id AS TokenId,
            rt.UserId,
            rt.ExpiresAt,
            rt.RevokedAt,
            u.Email,
            u.IsActive
        FROM RefreshTokens rt
        INNER JOIN Users u ON rt.UserId = u.Id
        WHERE rt.TokenHash = @TokenHash;";

            var tokenRecord = await sqlExecutor.QuerySingleAsync<RefreshTokenDto>(
                getRefreshTokenSql,
                new { TokenHash = tokenHash }
            );

            // 2. Security Checks
            if (tokenRecord is null) return null; // Token does not exist

            // Reuse Detection: If token was already revoked, someone may have stolen it.
            // Revoke ALL user tokens immediately as a safety precaution.
            if (tokenRecord.RevokedAt is not null)
            {
                await LogoutAsync(tokenRecord.UserId, null, cancellationToken);
                return null;
            }

            // Expiration or Inactive User check
            if (DateTime.UtcNow >= tokenRecord.ExpiresAt || !tokenRecord.IsActive)
            {
                return null;
            }

            // 3. Generate new Access Token and new Refresh Token (Token Rotation)
            var newAccessToken = tokenGenerator.GenerateJwtToken(tokenRecord.UserId, tokenRecord.Email);
            var newRawRefreshToken = tokenGenerator.GenerateRefreshTokenString();
            var newRefreshTokenHash = tokenGenerator.HashToken(newRawRefreshToken);

            var newRefreshTokenExpiresAt = DateTime.UtcNow.AddDays(
                tokenGenerator._jwtOptions.RefreshTokenExpiryDays > 0 ? tokenGenerator._jwtOptions.RefreshTokenExpiryDays : 7
            );

            // 4. Database Transaction: Revoke old token & Insert new rotated token
            const string rotateTokensSql = @"
        UPDATE RefreshTokens 
        SET RevokedAt = GETUTCDATE(), 
            ReplacedByTokenHash = @NewTokenHash,
            ReasonRevoked = 'Replaced by new token'
        WHERE Id = @OldTokenId;

        INSERT INTO RefreshTokens (UserId, TokenHash, ExpiresAt, CreatedAt)
        VALUES (@UserId, @NewTokenHash, @ExpiresAt, GETUTCDATE());";

            await sqlExecutor.ExecuteAsync(rotateTokensSql, new
            {
                OldTokenId = tokenRecord.TokenId,
                UserId = tokenRecord.UserId,
                NewTokenHash = newRefreshTokenHash,
                ExpiresAt = newRefreshTokenExpiresAt
            });

            return new AuthenticationResult(
                newAccessToken,
                newRawRefreshToken,
                newRefreshTokenExpiresAt,
                tokenRecord.Email,
                tokenRecord.UserId
            );
        }
    }
}
