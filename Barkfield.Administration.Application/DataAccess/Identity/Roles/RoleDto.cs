namespace Barkfield.Administration.Application.DataAccess.Identity.Roles;

/// <summary>
/// A role, for the assignment picker and for showing what a user holds.
/// </summary>
public class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Seeded roles the application relies on. The UI should not offer to delete these.</summary>
    public bool IsSystemRole { get; set; }
}
