namespace Barkfield.Administration.API.Models.Requests.Square
{
    public record SearchSquareCustomerRequest(
     string Email,
     string? PhoneNumber = null
 );
}
