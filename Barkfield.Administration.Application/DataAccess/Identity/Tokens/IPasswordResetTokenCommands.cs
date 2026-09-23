namespace Barkfield.Administration.Application.DataAccess.Identity.Tokens;

public interface IPasswordResetTokenCommands
{
    Task CreateAsync(PasswordResetTokenDto token, CancellationToken cancellationToken = default);

    Task MarkUsedAsync(Guid tokenId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invalidates any outstanding tokens for a user. Called before issuing a new one, so a
    /// fresh request supersedes an older link rather than leaving several live at once.
    /// </summary>
    Task InvalidateForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
