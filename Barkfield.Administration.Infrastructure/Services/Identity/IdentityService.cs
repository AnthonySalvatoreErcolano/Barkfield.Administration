using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.Entities.Identity.Tokens;
using Barkfield.Administration.Domain.Shared.Exceptions;
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
    internal class IdentityService(
        IPasswordHasher _passwordHasher,
        ITokenGenerator tokenGenerator,
        IUserQueries _userQueries,
        IUserCommands _userCommands,
        ISqlExecutor sqlExecutor,
        IOptions<JwtSettings> jwtOptions) : IIdentityService
    {
        private readonly JwtSettings _jwtSettings = jwtOptions.Value;


        public async Task<AuthenticationResult?> LoginAsync(string email,string password)
        {


            LoginDto? user = await sqlExecutor.QuerySingleAsync<LoginDto>(@"SELECT Id, Name, Email, PasswordHash, IsActive 
            FROM Users 
            WHERE Email = @Email AND IsActive=1;", new { Email = email });

            if (user == null) return null;

            var verificationResult = _passwordHasher.VerifyPassword( password,user.PasswordHash);

            if (!verificationResult) return null;

            var accessToken = tokenGenerator.GenerateJwtToken(user.Id, email);
            var rawRefreshToken = tokenGenerator.GenerateRefreshTokenString();
            var refreshTokenHash = tokenGenerator.HashToken(rawRefreshToken);

            var refreshTokenExpiresAt = DateTime.UtcNow.AddDays(_jwtSettings.RefreshTokenExpiryDays > 0 ? _jwtSettings.RefreshTokenExpiryDays : 7);
            
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
            string hashedPassword = _passwordHasher.HashPassword(password);
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
                await LogoutAsync(tokenRecord.UserId.ToString(), null, cancellationToken);
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
                _jwtSettings.RefreshTokenExpiryDays > 0 ? _jwtSettings.RefreshTokenExpiryDays : 7
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
        // TODO(chunk 2 - Identity): password reset is not implemented. It needs an
        // IEmailService abstraction plus IUserQueries.GetByEmailWithResetTokenAsync and
        // IUserCommands.CreateResetToken / UpdatePasswordAsync, none of which exist yet.
        // The original draft is preserved below.
        public Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Password reset request is not implemented yet.");
        }

        public Task CompletePasswordResetAsync(string email, string rawToken, string newPassword, CancellationToken cancellationToken)
        {
            throw new NotImplementedException("Password reset completion is not implemented yet.");
        }

        #region Original password-reset draft (does not compile; kept for reference)
//         public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
//         {
//             UserDto? dto = await _userQueries.GetUserByEmailAsync(email, cancellationToken);
// 
//             // Prevent email enumeration: return silently if user does not exist
//             if (dto is null) return;
// 
//             // 1. Generate raw token and computed hash via ITokenHasher
//             string rawToken = tokenGenerator.GenerateRawToken();
//             string tokenHash = tokenGenerator.HashToken(rawToken);
//             DateTime expiresAt = DateTime.UtcNow.AddHours(1);
//             var resetToken = PasswordResetToken.Create( userId: dto.Id, tokenHash: tokenHash, validityDuration:TimeSpan.FromHours(1));
// 
//         
//             await _userCommands.CreateResetToken(, cancellationToken);
// 
//             // 4. Send raw token to user via email infrastructure
//             await _emailService.SendPasswordResetEmailAsync(dto.Email, rawToken, cancellationToken);
//         }
// 
//         public async Task CompletePasswordResetAsync(string email, string rawToken, string newPassword, CancellationToken cancellationToken)
//         {
//             UserDto? dto = await _userQueries.GetByEmailWithResetTokenAsync(email, cancellationToken);
//             if (dto is null || string.IsNullOrWhiteSpace(dto.PasswordResetTokenHash) || !dto.PasswordResetTokenExpiresAt.HasValue)
//             {
//                 throw new DomainException("Invalid password reset request.");
//             }
// 
//             // 1. Verify expiration window
//             if (DateTime.UtcNow > dto.PasswordResetTokenExpiresAt.Value)
//             {
//                 throw new DomainException("The password reset token has expired.");
//             }
// 
//             // 2. Verify incoming raw token against stored hash using ITokenHasher
//             bool isTokenValid = _tokenHasher.VerifyToken(rawToken, dto.PasswordResetTokenHash);
//             if (!isTokenValid)
//             {
//                 throw new DomainException("Invalid password reset token.");
//             }
// 
//             // 3. Hash new password via IPasswordHasher
//             string newPasswordHash = _passwordHasher.HashPassword(newPassword);
// 
//             // 4. Rehydrate domain aggregate to enforce core domain validation on profile update
//             IEnumerable<Guid> roles = await _userQueries.GetUserRoles(dto.Id, cancellationToken);
//             User user = User.FromDto(dto.Id, dto.Name, dto.Email, newPasswordHash, dto.IsActive, dto.CreatedAt, roles);
// 
//             // 5. Persist updated password and clear reset token fields
//             var command = new UpdateUserPasswordCommand(user.Id, user.PasswordHash);
//             await _userCommands.UpdatePasswordAsync(command, cancellationToken);
//         }
        #endregion
    }
}
