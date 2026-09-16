namespace Barkfield.Administration.API.Models.Requests.Customers
{
    public record CreateCustomerRequest(
     string FirstName,
     string LastName,
     string Email,
     string? PhoneNumber = null,
     string? AddressLine1 = null,
     string? AddressLine2 = null,
     string? City = null,
     string? State = null,
     string? PostalCode = null,
     string? Notes = null,
     string? SquareCustomerId = null
 );
}
