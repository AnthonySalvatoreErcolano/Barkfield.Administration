namespace Barkfield.Administration.Application.DataAccess.Users;

/// <summary>
/// One row on the staff user list.
/// </summary>
/// <remarks>
/// Carries no password hash. Read DTOs for users deliberately omit it so it cannot be
/// serialised to a client by accident — the only type that exposes it is
/// <see cref="UserCredentialsDto"/>, which never leaves the sign-in path.
/// </remarks>
public class UserListItemDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }

    /// <summary>Comma-separated role names, assembled by the query for display.</summary>
    public string Roles { get; set; } = string.Empty;
}
