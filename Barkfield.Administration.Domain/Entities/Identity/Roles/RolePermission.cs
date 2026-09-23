namespace Barkfield.Administration.Domain.Entities.Identity.Roles;

/// <summary>
/// Grants one permission to one role.
/// </summary>
public class RolePermission
{
    public Guid RoleId { get; set; }
    public int PermissionId { get; set; }
}
