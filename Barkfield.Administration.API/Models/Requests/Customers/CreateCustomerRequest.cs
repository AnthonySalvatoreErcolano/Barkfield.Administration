namespace Barkfield.Administration.API.Models.Requests.Customers
{
    public record CreateCustomerRequest(
     string FirstName,
     string LastName,
     string Email,
     string? PhoneNumber = null,
     string? Address = null,
     string? City = null,
     string? State = null,
     string? PostalCode = null,
     string? Notes = null,
     string? SquareCustomerId = null
 );
}
