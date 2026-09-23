using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Customers;

/// <summary>
/// The customer id comes from the route, not the body.
/// </summary>
/// <remarks>
/// The Square link is not editable here — it is managed by the sync endpoint, so an edit
/// form cannot silently repoint a customer at a different Square profile.
/// </remarks>
public class UpdateCustomerRequest
{
    [Required, MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [MaxLength(20)]
    public string? PhoneNumber { get; set; }

    public string? Notes { get; set; }

    [MaxLength(200)] public string? Street { get; set; }
    [MaxLength(100)] public string? City { get; set; }
    [MaxLength(50)]  public string? State { get; set; }
    [MaxLength(20)]  public string? ZipCode { get; set; }
}
