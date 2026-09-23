namespace Barkfield.Administration.Application.Services.Email;

/// <summary>
/// Outbound transactional email.
/// </summary>
/// <remarks>
/// Deliberately narrow: named methods per message rather than a generic Send, so the call
/// sites cannot invent their own copy and templates stay in one place.
/// </remarks>
public interface IEmailService
{
    /// <summary>
    /// Sends a password reset link. <paramref name="resetToken"/> is the raw token — only its
    /// hash is stored, so this is the one moment it exists in the clear.
    /// </summary>
    Task SendPasswordResetAsync(string toEmail, string recipientName, string resetToken, DateTime expiresAt, CancellationToken cancellationToken = default);

    /// <summary>Sends a newly created staff member their sign-in details.</summary>
    Task SendWelcomeAsync(string toEmail, string recipientName, CancellationToken cancellationToken = default);
}
