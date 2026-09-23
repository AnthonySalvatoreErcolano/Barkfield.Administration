using Barkfield.Administration.Application.DataAccess.Identity.Roles;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Email;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// Staff account management.
/// </summary>
public class UserService
{
    private readonly IUserQueries _userQueries;
    private readonly IUserCommands _userCommands;
    private readonly IRoleQueries _roleQueries;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IEmailService _emailService;
    private readonly ILogger<UserService> _logger;

    public UserService(
        IUserQueries userQueries,
        IUserCommands userCommands,
        IRoleQueries roleQueries,
        IPasswordHasher passwordHasher,
        IEmailService emailService,
        ILogger<UserService> logger)
    {
        _userQueries = userQueries;
        _userCommands = userCommands;
        _roleQueries = roleQueries;
        _passwordHasher = passwordHasher;
        _emailService = emailService;
        _logger = logger;
    }

    public async Task<Guid> CreateUserAsync(
        string name,
        string email,
        string password,
        IEnumerable<Guid> roleIds,
        bool isAdmin = false,
        CancellationToken cancellationToken = default)
    {
        if (await _userQueries.EmailExistsAsync(email, null, cancellationToken))
        {
            throw new ValidationException($"A user with the email '{email}' already exists.");
        }

        await EnsureRolesExistAsync(roleIds, cancellationToken);

        string passwordHash = _passwordHasher.HashPassword(password);
        User user = User.Create(name, email, passwordHash, roleIds, isAdmin);

        await _userCommands.CreateAsync(user, cancellationToken);

        try
        {
            await _emailService.SendWelcomeAsync(user.Email, user.Name, cancellationToken);
        }
        catch (Exception ex)
        {
            // The account exists and is usable; a failed welcome email must not undo it.
            _logger.LogError(ex, "User {UserId} was created but the welcome email failed.", user.Id);
        }

        return user.Id;
    }

    public async Task UpdateUserAsync(
        Guid userId,
        string name,
        string email,
        IEnumerable<Guid> roleIds,
        bool isAdmin,
        CancellationToken cancellationToken = default)
    {
        User user = await LoadAsync(userId, cancellationToken);

        if (await _userQueries.EmailExistsAsync(email, userId, cancellationToken))
        {
            throw new ValidationException($"Another user already uses the email '{email}'.");
        }

        await EnsureRolesExistAsync(roleIds, cancellationToken);

        user.UpdateProfile(name, email);
        user.SyncRoles(roleIds);

        if (isAdmin) user.GrantAdmin(); else user.RevokeAdmin();

        await _userCommands.UpdateAsync(user, cancellationToken);
    }

    /// <summary>
    /// Changes a user's own password, verifying the current one first.
    /// </summary>
    public async Task ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default)
    {
        UserCredentialsDto credentials = await _userQueries.GetCredentialsByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException($"User with ID '{userId}' was not found.");

        if (!_passwordHasher.VerifyPassword(currentPassword, credentials.PasswordHash))
        {
            throw new ValidationException("The current password is incorrect.");
        }

        await _userCommands.UpdatePasswordAsync(
            userId, _passwordHasher.HashPassword(newPassword), cancellationToken);
    }

    /// <summary>
    /// Deactivates an account. Sign-in is refused from that moment, and the caller should
    /// also revoke the user's refresh tokens so existing sessions cannot be extended.
    /// </summary>
    public async Task DeactivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _userCommands.SetActiveAsync(userId, false, cancellationToken))
        {
            throw new NotFoundException($"User with ID '{userId}' was not found.");
        }
    }

    public async Task ReactivateUserAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (!await _userCommands.SetActiveAsync(userId, true, cancellationToken))
        {
            throw new NotFoundException($"User with ID '{userId}' was not found.");
        }
    }

    /// <summary>
    /// Rejects unknown role ids up front, so a bad request fails with a clear message rather
    /// than a foreign key violation from the database.
    /// </summary>
    private async Task EnsureRolesExistAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken)
    {
        var requested = roleIds?.Distinct().ToList() ?? [];

        if (requested.Count == 0)
        {
            throw new ValidationException("A user must be assigned at least one role.");
        }

        var existing = await _roleQueries.GetExistingIdsAsync(requested, cancellationToken);
        var missing = requested.Except(existing).ToList();

        if (missing.Count > 0)
        {
            throw new ValidationException($"Unknown role id(s): {string.Join(", ", missing)}.");
        }
    }

    private async Task<User> LoadAsync(Guid userId, CancellationToken cancellationToken)
    {
        UserCredentialsDto credentials = await _userQueries.GetCredentialsByIdAsync(userId, cancellationToken)
            ?? throw new NotFoundException($"User with ID '{userId}' was not found.");

        var roleIds = await _userQueries.GetRoleIdsAsync(userId, cancellationToken);

        return User.FromDto(
            credentials.Id,
            credentials.Name,
            credentials.Email,
            credentials.PasswordHash,
            credentials.IsActive,
            credentials.IsAdmin,
            credentials.CreatedAt,
            credentials.UpdatedAt,
            roleIds);
    }
}
