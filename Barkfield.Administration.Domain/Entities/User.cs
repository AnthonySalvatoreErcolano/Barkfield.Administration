using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A staff account. Drivers are users too, holding the driver role — there is no separate
/// identity system for them.
/// </summary>
public class User
{
    private readonly List<Guid> _roleIds = [];

    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;

    /// <summary>
    /// BCrypt hash. Never leaves the Application layer — read DTOs deliberately omit it.
    /// </summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public bool IsActive { get; private set; }

    /// <summary>
    /// Bypasses every permission check. The authorization filter honours this directly, so
    /// it is deliberately not grantable through the roles UI.
    /// </summary>
    public bool IsAdmin { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<Guid> RoleIds => _roleIds.AsReadOnly();

    private User() { }

    public static User Create(
        string name,
        string email,
        string passwordHash,
        IEnumerable<Guid> roleIds,
        bool isAdmin = false)
    {
        ValidateName(name);
        ValidateEmail(email);

        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("A password hash is required.");

        var roleList = roleIds?.Distinct().ToList() ?? [];
        if (roleList.Count == 0)
            throw new DomainException("A user must be assigned at least one role.");

        var user = new User
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Email = email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHash,
            IsActive = true,
            IsAdmin = isAdmin,
            CreatedAt = DateTime.UtcNow
        };

        user._roleIds.AddRange(roleList);

        return user;
    }

    public static User FromDto(
        Guid id,
        string name,
        string email,
        string passwordHash,
        bool isActive,
        bool isAdmin,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<Guid>? roleIds)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid user ID.");

        var user = new User
        {
            Id = id,
            Name = name,
            Email = email,
            PasswordHash = passwordHash,
            IsActive = isActive,
            IsAdmin = isAdmin,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (roleIds is not null)
        {
            user._roleIds.AddRange(roleIds.Distinct());
        }

        return user;
    }

    public void UpdateProfile(string name, string email)
    {
        ValidateName(name);
        ValidateEmail(email);

        Name = name.Trim();
        Email = email.Trim().ToLowerInvariant();
        Touch();
    }

    /// <summary>
    /// Replaces the user's roles wholesale. A user must always hold at least one.
    /// </summary>
    public void SyncRoles(IEnumerable<Guid> roleIds)
    {
        var roleList = roleIds?.Distinct().ToList() ?? [];

        if (roleList.Count == 0)
            throw new DomainException("A user must be assigned at least one role.");

        if (roleList.Any(id => id == Guid.Empty))
            throw new DomainException("Invalid role ID.");

        _roleIds.Clear();
        _roleIds.AddRange(roleList);
        Touch();
    }

    public void AssignRole(Guid roleId)
    {
        if (roleId == Guid.Empty)
            throw new DomainException("Invalid role ID.");

        if (_roleIds.Contains(roleId)) return;

        _roleIds.Add(roleId);
        Touch();
    }

    public void RemoveRole(Guid roleId)
    {
        if (_roleIds.Count <= 1 && _roleIds.Contains(roleId))
            throw new DomainException("Cannot remove the last role from a user.");

        if (_roleIds.Remove(roleId))
        {
            Touch();
        }
    }

    /// <summary>
    /// Replaces the stored hash. The caller hashes — the domain never sees a plain password.
    /// </summary>
    public void ChangePassword(string passwordHash)
    {
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("A password hash is required.");

        PasswordHash = passwordHash;
        Touch();
    }

    /// <summary>
    /// Deactivates the account. Sign-in is refused and existing sessions should be revoked
    /// by the caller; the user is never deleted, because audit history references them.
    /// </summary>
    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        Touch();
    }

    public void Reactivate()
    {
        if (IsActive) return;

        IsActive = true;
        Touch();
    }

    public void GrantAdmin()
    {
        if (IsAdmin) return;

        IsAdmin = true;
        Touch();
    }

    public void RevokeAdmin()
    {
        if (!IsAdmin) return;

        IsAdmin = false;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("User name cannot be empty.");

        if (name.Trim().Length > 200)
            throw new DomainException("User name cannot exceed 200 characters.");
    }

    private static void ValidateEmail(string email)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            throw new DomainException("A valid email address is required.");
    }
}
