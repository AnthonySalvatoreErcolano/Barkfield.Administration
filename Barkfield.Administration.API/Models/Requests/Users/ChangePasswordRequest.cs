using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Users;

public class ChangePasswordRequest
{
    [Required]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, MinLength(12), MaxLength(128)]
    public string NewPassword { get; set; } = string.Empty;
}
