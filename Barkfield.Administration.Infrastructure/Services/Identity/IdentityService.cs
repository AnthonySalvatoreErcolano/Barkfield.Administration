using Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens;
using Barkfield.Administration.Application.DataAccess.Identity.Tokens;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Services.Email;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Domain.Entities.Identity.Tokens;
using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Barkfield.Administration.Infrastructure.Services.Identity;

/// <summary>
/// Sign-in, session lifetime and password reset.
/// </summary>
/// <remarks>
/// <para>
/// Access tokens are short-lived JWTs; sessions are extended with a rotating refresh token
/// stored only as a SHA-256 hash. Presenting an already-revoked token is treated as theft
/// and revokes every token for that user.
/// </para>
/// <para>
/// The password reset flow is deliberately silent about whether an email is registered:
/// every request returns the same response, so the endpoint cannot be used to enumerate
/// accounts.
/// </para>
/// </remarks>
internal class IdentityService : IIdentityService
{
    private static readonly TimeSpan ResetTokenLifetime = TimeSpan.FromHours(1);

    private readonly IPasswordHasher _passwordHasher;
    private readonly ITokenGenerator _tokenGenerator;
    private readonly IUserQueries _userQueries;
    private readonly IUserCommands _userCommands;
    private readonly IRefreshTokenQueries _refreshTokenQueries;
    private readonly IRefreshTokenCommands _refreshTokenCommands;
    private readonly IPasswordResetTokenQueries _resetTokenQueries;
    private readonly IPasswordResetTokenCommands _resetTokenCommands;
    private readonly IEmailService _emailService;
    private readonly ILogger<IdentityService> _logger;
    private readonly JwtSettings _jwtSettings;

    public IdentityService(
        IPasswordHasher passwordHasher,
        ITokenGenerator tokenGenerator,
        IUserQueries userQueries,
        IUserCommands userCommands,
        IRefreshTokenQueries refreshTokenQueries,
        IRefreshTokenCommands refreshTokenCommands,
        IPasswordResetTokenQueries resetTokenQueries,
        IPasswordResetTokenCommands resetTokenCommands,
        IEmailService emailService,
        ILogger<IdentityService> logger,
        IOptions<JwtSettings> jwtOptions)
    {
        _passwordHasher = passwordHasher;
        _tokenGenerator = tokenGenerator;
        _userQueries = userQueries;
        _userCommands = userCommands;
        _refreshTokenQueries = refreshTokenQueries;
        _refreshTokenCommands = refreshTokenCommands;
        _resetTokenQueries = resetTokenQueries;
        _resetTokenCommands = resetTokenCommands;
        _emailService = emailService;
        _logger = logger;
        _jwtSettings = jwtOptions.Value;
    }

    private int RefreshTokenDays => _jwtSettings.RefreshTokenExpiryDays > 0 ? _jwtSettings.RefreshTokenExpiryDays : 7;

    public async Task<AuthenticationResult?> LoginAsync(string email, string password)
    {
        UserCredentialsDto? user = await _userQueries.GetCredentialsByEmailAsync(email, CancellationToken.None);

        // Verify even when the user is missing or inactive, so the response time does not
        // reveal which emails are registered.
        bool passwordMatches = _passwordHasher.VerifyPassword(
            password,
            user?.PasswordHash ?? "$2a$12$0000000000000000000000000000000000000000000000000000");

        if (user is null || !user.IsActive || !passwordMatches)
        {
            return null;
        }

        return await IssueSessionAsync(user.Id, user.Email, CancellationToken.None);
    }

    public async Task LogoutAsync(string? userId, string? refreshToken, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            await _refreshTokenCommands.RevokeTokenAsync(
                _tokenGenerator.HashToken(refreshToken), "User logged out", null, null, cancellationToken);

            return;
        }

        if (!string.IsNullOrWhiteSpace(userId))
        {
            await _refreshTokenCommands.RevokeAllUserTokensAsync(
                userId, "User global logout", null, cancellationToken);
        }
    }

    public async Task<AuthenticationResult?> RefreshTokenAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) return null;

        string tokenHash = _tokenGenerator.HashToken(refreshToken);

        RefreshToken? stored = await _refreshTokenQueries.GetByHashAsync(tokenHash, cancellationToken);
        if (stored is null) return null;

        // Reuse detection: a token that has already been rotated away should never be
        // presented again. Treat it as a stolen token and end every session for that user.
        if (stored.RevokedAt is not null)
        {
            _logger.LogWarning(
                "Refresh token reuse detected for user {UserId}. Revoking all their sessions.", stored.UserId);

            await _refreshTokenCommands.RevokeAllUserTokensAsync(
                stored.UserId.ToString(), "Token reuse detected", null, cancellationToken);

            return null;
        }

        if (DateTime.UtcNow >= stored.ExpiresAt) return null;

        UserCredentialsDto? user = await _userQueries.GetCredentialsByIdAsync(stored.UserId, cancellationToken);
        if (user is null || !user.IsActive) return null;

        AuthenticationResult result = await IssueSessionAsync(user.Id, user.Email, cancellationToken);

        await _refreshTokenCommands.RevokeTokenAsync(
            tokenHash,
            "Replaced by new token",
            null,
            _tokenGenerator.HashToken(result.RefreshToken),
            cancellationToken);

        return result;
    }

    public async Task<Guid?> RegisterAsync(string email, string password, string name) =>
        throw new NotSupportedException(
            "Self-registration is not offered. Staff accounts are created by an administrator through the users endpoint.");

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        UserCredentialsDto? user = await _userQueries.GetCredentialsByEmailAsync(email, cancellationToken);

        // Return silently for unknown or inactive accounts. The caller always reports the
        // same thing, so this endpoint cannot be used to discover which emails exist.
        if (user is null || !user.IsActive) return;

        // A new request supersedes any outstanding link rather than leaving several live.
        await _resetTokenCommands.InvalidateForUserAsync(user.Id, cancellationToken);

        string rawToken = _tokenGenerator.GenerateRawToken();
        string tokenHash = _tokenGenerator.HashToken(rawToken);

        PasswordResetToken token = PasswordResetToken.Create(user.Id, tokenHash, ResetTokenLifetime);

        await _resetTokenCommands.CreateAsync(
            new PasswordResetTokenDto
            {
                Id = token.Id,
                UserId = token.UserId,
                TokenHash = token.TokenHash,
                ExpiresAt = token.ExpiresAt,
                IsUsed = token.IsUsed,
                CreatedAt = token.CreatedAt
            },
            cancellationToken);

        // Only the hash is stored, so this is the one moment the raw token exists.
        await _emailService.SendPasswordResetAsync(
            user.Email, user.Name, rawToken, token.ExpiresAt, cancellationToken);
    }

    public async Task CompletePasswordResetAsync(
        string email,
        string rawToken,
        string newPassword,
        CancellationToken cancellationToken)
    {
        UserCredentialsDto? user = await _userQueries.GetCredentialsByEmailAsync(email, cancellationToken);
        if (user is null || !user.IsActive)
        {
            throw new DomainException("Invalid or expired password reset request.");
        }

        PasswordResetTokenDto? stored = await _resetTokenQueries.GetActiveForUserAsync(user.Id, cancellationToken);
        if (stored is null)
        {
            throw new DomainException("Invalid or expired password reset request.");
        }

        if (!_tokenGenerator.VerifyToken(rawToken, stored.TokenHash))
        {
            throw new DomainException("Invalid or expired password reset request.");
        }

        await _userCommands.UpdatePasswordAsync(
            user.Id, _passwordHasher.HashPassword(newPassword), cancellationToken);

        await _resetTokenCommands.MarkUsedAsync(stored.Id, cancellationToken);

        // A password change ends every existing session — that is the point of resetting it.
        await _refreshTokenCommands.RevokeAllUserTokensAsync(
            user.Id.ToString(), "Password was reset", null, cancellationToken);
    }

    private async Task<AuthenticationResult> IssueSessionAsync(Guid userId, string email, CancellationToken cancellationToken)
    {
        string accessToken = _tokenGenerator.GenerateJwtToken(userId, email);
        string rawRefreshToken = _tokenGenerator.GenerateRefreshTokenString();
        DateTime expiresAt = DateTime.UtcNow.AddDays(RefreshTokenDays);

        await _refreshTokenCommands.CreateAsync(
            new RefreshToken
            {
                UserId = userId,
                TokenHash = _tokenGenerator.HashToken(rawRefreshToken),
                ExpiresAt = expiresAt,
                CreatedAt = DateTime.UtcNow
            },
            cancellationToken);

        return new AuthenticationResult(accessToken, rawRefreshToken, expiresAt, email, userId);
    }
}
