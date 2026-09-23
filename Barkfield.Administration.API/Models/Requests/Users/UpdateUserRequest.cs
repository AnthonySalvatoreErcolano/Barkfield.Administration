using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Users;

/// <summary>
/// The user id comes from the route. Passwords are not changed here — a user changes their
/// own through change-password, and a forgotten one goes through the reset flow.
/// </summary>
public class UpdateUserRequest
{
    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required, MinLength(1)]
    public IEnumerable<Guid> RoleIds { get; set; } = [];

    public bool IsAdmin { get; set; }
}
