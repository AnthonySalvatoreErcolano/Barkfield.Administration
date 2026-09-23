namespace Barkfield.Administration.Application.DataAccess.Identity.Tokens;

public interface IPasswordResetTokenQueries
{
    /// <summary>
    /// The most recent unused, unexpired token for a user, or null.
    /// </summary>
    /// <remarks>
    /// Looked up by user rather than by token hash so the reset endpoint can verify the
    /// supplied token in constant time against the stored hash, rather than leaking whether
    /// a given token exists.
    /// </remarks>
    Task<PasswordResetTokenDto?> GetActiveForUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
