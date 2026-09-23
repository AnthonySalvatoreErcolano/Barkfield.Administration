namespace Barkfield.Administration.Application.DataAccess.Users;

/// <summary>
/// The only read model carrying a password hash.
/// </summary>
/// <remarks>
/// Used by the sign-in and password-reset paths and nowhere else. It is never returned from
/// a controller — keeping the hash on a separate type means it cannot leak through a
/// serialised response by accident, which is exactly what the previous shared UserDto did.
/// </remarks>
public class UserCredentialsDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public bool IsAdmin { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
