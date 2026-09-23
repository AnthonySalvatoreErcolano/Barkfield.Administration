using Barkfield.Administration.Application.Common;

namespace Barkfield.Administration.Application.DataAccess.Users;

public interface IUserQueries
{
    Task<PagedResult<UserListItemDto>> SearchAsync(UserFilter filter, CancellationToken cancellationToken = default);

    Task<UserDetailDto?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<UserDetailDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the password hash for sign-in and password reset. The only query that exposes
    /// it — everything user-facing uses <see cref="UserDetailDto"/>, which omits it.
    /// </summary>
    Task<UserCredentialsDto?> GetCredentialsByEmailAsync(string email, CancellationToken cancellationToken = default);

    Task<UserCredentialsDto?> GetCredentialsByIdAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<bool> EmailExistsAsync(string email, Guid? excludingUserId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyCollection<Guid>> GetRoleIdsAsync(Guid userId, CancellationToken cancellationToken = default);
}
