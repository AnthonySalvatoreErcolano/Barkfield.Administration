using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Customers;
using Barkfield.Administration.API.Models.Requests.Square;
using Barkfield.Administration.API.Models.Responses.Customers;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Customer records, their pets, and keeping the linked Square profile in step.
/// </summary>
/// <remarks>
/// Endpoints are deliberately granular and resource-shaped. Page-level composition —
/// pulling a customer together with their subscriptions and delivery history — is done by
/// the UI's own layer, which calls several of these.
/// </remarks>
[ApiController]
[Route("api/customers")]
[Authorize]
public class CustomersController(ICustomerQueries customerQueries, CustomerService customerService) : ControllerBase
{
    private readonly ICustomerQueries _customerQueries = customerQueries;
    private readonly CustomerService _customerService = customerService;

    /// <summary>
    /// Returns a paged, searchable, sortable list of customers for the dashboard.
    /// </summary>
    /// <param name="filter">
    /// Search, filter, sort and paging, from the query string. Deactivated customers are
    /// excluded unless <c>includeInactive</c> is set. Sort keys: name, email, createdAt,
    /// city, phone — anything else falls back to name.
    /// </param>
    [HttpGet]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(PagedResult<CustomerListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetCustomers(
        [FromQuery] CustomerFilter filter,
        CancellationToken cancellationToken)
    {
        PagedResult<CustomerListItemDto> result = await _customerQueries.SearchAsync(filter, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns one customer with their pets.
    /// </summary>
    [HttpGet("{customerId:guid}", Name = nameof(GetCustomerById))]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerById(
        [FromRoute] Guid customerId,
        CancellationToken cancellationToken)
    {
        CustomerDetailDto? customer = await _customerQueries.GetByIdAsync(customerId, cancellationToken);

        if (customer is null)
        {
            return NotFound(new { message = $"Customer with ID '{customerId}' was not found." });
        }

        return Ok(customer);
    }

    /// <summary>
    /// Looks a customer up by exact email address. Includes deactivated customers, so this
    /// can be used as a duplicate check.
    /// </summary>
    [HttpGet("by-email")]
    [RequirePermission(Permissions.Customers.View)]
    [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCustomerByEmail(
        [FromQuery] string email,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "An email address is required." });
        }

        CustomerDetailDto? customer = await _customerQueries.GetByEmailAsync(email, cancellationToken);

        if (customer is null)
        {
            return NotFound(new { message = $"No customer found with the email '{email}'." });
        }

        return Ok(customer);
    }

    /// <summary>
    /// Searches Square for existing profiles matching the contact details, so staff can
    /// link an existing customer rather than create a duplicate.
    /// </summary>
    [HttpPost("search-square")]
    [RequirePermission(Permissions.Customers.Create)]
    [ProducesResponseType(typeof(IEnumerable<SquareCustomerCandidateDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchSquareCustomers(
        [FromBody] SearchSquareCustomerRequest request,
        CancellationToken cancellationToken)
    {
        var candidates = await _customerService.SearchSquareCustomersAsync(
            request.Email, request.PhoneNumber, cancellationToken);

        return Ok(candidates);
    }

    /// <summary>
    /// Creates a customer and provisions or links their Square profile.
    /// </summary>
    /// <remarks>
    /// The customer is saved locally first. If Square is unreachable the request still
    /// succeeds and the response carries <c>squareSynced: false</c> — staff are not blocked
    /// by an outage, and the link can be repaired from the sync endpoint.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.Customers.Create)]
    [ProducesResponseType(typeof(CustomerWriteResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateCustomer(
        [FromBody] CreateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        CustomerWriteResult result = await _customerService.CreateCustomerAsync(
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            request.Notes,
            BuildAddress(request.Street, request.City, request.State, request.ZipCode),
            request.SquareCustomerId,
            cancellationToken);

        return CreatedAtRoute(
            nameof(GetCustomerById),
            new { customerId = result.CustomerId },
            ToResponse(result));
    }

    /// <summary>
    /// Updates a customer and pushes the change to Square when they are linked.
    /// </summary>
    [HttpPut("{customerId:guid}")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(typeof(CustomerWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateCustomer(
        [FromRoute] Guid customerId,
        [FromBody] UpdateCustomerRequest request,
        CancellationToken cancellationToken)
    {
        CustomerWriteResult result = await _customerService.UpdateCustomerAsync(
            customerId,
            request.FirstName,
            request.LastName,
            request.Email,
            request.PhoneNumber,
            request.Notes,
            BuildAddress(request.Street, request.City, request.State, request.ZipCode),
            cancellationToken);

        return Ok(ToResponse(result));
    }

    /// <summary>
    /// Deactivates a customer. This is a soft delete — their delivery history is retained
    /// and they can be reactivated.
    /// </summary>
    [HttpDelete("{customerId:guid}")]
    [RequirePermission(Permissions.Customers.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateCustomer(
        [FromRoute] Guid customerId,
        CancellationToken cancellationToken)
    {
        await _customerService.DeactivateCustomerAsync(customerId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reactivates a previously deactivated customer.
    /// </summary>
    [HttpPost("{customerId:guid}/reactivate")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateCustomer(
        [FromRoute] Guid customerId,
        CancellationToken cancellationToken)
    {
        await _customerService.ReactivateCustomerAsync(customerId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Retries the Square link for a customer whose earlier sync failed, or re-pushes the
    /// current details for one that is already linked.
    /// </summary>
    /// <remarks>
    /// Unlike create and update, this endpoint exists to do the Square work, so a Square
    /// failure here surfaces as an error rather than being swallowed.
    /// </remarks>
    [HttpPost("{customerId:guid}/sync-square")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(typeof(CustomerWriteResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> SyncCustomerToSquare(
        [FromRoute] Guid customerId,
        CancellationToken cancellationToken)
    {
        CustomerWriteResult result = await _customerService.SyncToSquareAsync(customerId, cancellationToken);

        return Ok(ToResponse(result));
    }

    /// <summary>
    /// Updates the driver-facing detail for this customer's stop: access notes, how long the
    /// stop takes, and the window they prefer.
    /// </summary>
    /// <remarks>
    /// A delivery always goes to the customer's own address, so this is where a gate code or a
    /// stop duration lives. All of it feeds the routing provider when the delivery is dispatched.
    /// </remarks>
    /// <response code="400">A window was given with only one end, or ends at or before it starts.</response>
    [HttpPut("{customerId:guid}/delivery-details")]
    [RequirePermission(Permissions.Customers.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateDeliveryDetails(
        [FromRoute] Guid customerId,
        [FromBody] UpdateDeliveryDetailsRequest request,
        CancellationToken cancellationToken)
    {
        await _customerService.UpdateDeliveryDetailsAsync(
            customerId,
            request.AccessNotes,
            request.ServiceDurationMinutes,
            request.PreferredWindowStart,
            request.PreferredWindowEnd,
            cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Builds the Address value object, or null when no address fields were supplied.
    /// </summary>
    private static Address? BuildAddress(string? street, string? city, string? state, string? zipCode)
    {
        if (string.IsNullOrWhiteSpace(street)
            && string.IsNullOrWhiteSpace(city)
            && string.IsNullOrWhiteSpace(state)
            && string.IsNullOrWhiteSpace(zipCode))
        {
            return null;
        }

        return new Address(
            street ?? string.Empty,
            city ?? string.Empty,
            state ?? string.Empty,
            zipCode ?? string.Empty);
    }

    private static CustomerWriteResponse ToResponse(CustomerWriteResult result) => new()
    {
        CustomerId = result.CustomerId,
        SquareSynced = result.SquareSynced,
        SquareError = result.SquareError
    };
}
