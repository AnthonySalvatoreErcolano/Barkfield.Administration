using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities.Identity.Roles;

/// <summary>
/// A named set of permissions a user can hold, e.g. Manager or Driver.
/// </summary>
/// <remarks>
/// Uses a <see cref="Guid"/> id because role ids travel through the API on user create and
/// edit. Permissions, by contrast, are internal reference data keyed by int and are never
/// addressed from outside.
/// </remarks>
public class Role
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;

    /// <summary>Seeded roles the application relies on and which staff cannot delete.</summary>
    public bool IsSystemRole { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private Role() { }

    public static Role Create(string name, string description = "", bool isSystemRole = false)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Role name is required.");

        return new Role
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            IsSystemRole = isSystemRole,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Role FromDto(Guid id, string name, string description, bool isSystemRole, DateTime createdAt, DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid role ID.");

        return new Role
        {
            Id = id,
            Name = name,
            Description = description,
            IsSystemRole = isSystemRole,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    public void Update(string name, string description)
    {
        if (IsSystemRole)
            throw new DomainException($"The '{Name}' role is built in and cannot be renamed.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Role name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }
}
