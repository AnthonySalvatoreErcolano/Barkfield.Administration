using Barkfield.Administration.Application.DataAccess.Identity.Roles;

namespace Barkfield.Administration.Application.DataAccess.Users;

/// <summary>
/// A single staff user with their assigned roles. Carries no password hash.
/// </summary>
public class UserDetailDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyCollection<RoleDto> Roles { get; set; } = [];

    public IReadOnlyCollection<Guid> RoleIds => Roles.Select(r => r.Id).ToList();
}
