using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Identity;

/// <summary>
/// Sign-in credentials.
/// </summary>
/// <remarks>
/// These models are defined here rather than borrowed from
/// <c>Microsoft.AspNetCore.Identity.Data</c>, so the validation rules and property names are
/// ours and cannot shift under a framework update.
/// </remarks>
public class LoginRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string Password { get; set; } = string.Empty;
}

public class ForgotPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordRequest
{
    [Required, EmailAddress]
    public string Email { get; set; } = string.Empty;

    /// <summary>The raw token from the reset email. Only its hash is stored server-side.</summary>
    [Required]
    public string ResetToken { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
