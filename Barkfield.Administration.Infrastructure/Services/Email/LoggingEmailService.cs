using Barkfield.Administration.Application.Services.Email;
using Microsoft.Extensions.Logging;

namespace Barkfield.Administration.Infrastructure.Services.Email;

/// <summary>
/// Placeholder email transport that logs instead of sending.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Not production ready.</b> No provider has been chosen yet (Azure Communication
/// Services, SendGrid and plain SMTP are all candidates). This exists so the password reset
/// and welcome flows are complete and testable end to end, and so swapping in a real
/// transport is one class and one DI line.
/// </para>
/// <para>
/// The reset link is logged at Warning level deliberately: in development that is how you
/// retrieve the token, and in production it is loud enough to notice that real emails are
/// not going out.
/// </para>
/// </remarks>
public class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendPasswordResetAsync(
        string toEmail,
        string recipientName,
        string resetToken,
        DateTime expiresAt,
        CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "EMAIL NOT SENT (no transport configured). Password reset for {Email} — token: {Token}, expires {ExpiresAt:u}",
            toEmail, resetToken, expiresAt);

        return Task.CompletedTask;
    }

    public Task SendWelcomeAsync(string toEmail, string recipientName, CancellationToken cancellationToken = default)
    {
        _logger.LogWarning(
            "EMAIL NOT SENT (no transport configured). Welcome message for {Name} <{Email}>.",
            recipientName, toEmail);

        return Task.CompletedTask;
    }
}
