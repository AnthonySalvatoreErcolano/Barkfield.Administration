using Barkfield.Administration.API.Models.Requests.Customers;
using Barkfield.Administration.API.Models.Requests.Square;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class Customers : ControllerBase
    {
        private readonly ICustomerQueries _customerQueries;

        public Customers(ICustomerQueries customerQueries)
        {
            _customerQueries = customerQueries;
        }

        [HttpGet("{customerId:guid}")]
        [ProducesResponseType(typeof(CustomerDetailDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetCustomerById([FromRoute] Guid customerId, CancellationToken cancellationToken)
        {
            CustomerDetailDto? customer = await _customerQueries.GetCustomerByIdAsync(customerId, cancellationToken);

            if (customer is null)
            {
                return NotFound(new { message = $"Customer with ID '{customerId}' was not found." });
            }

            return Ok(customer);
        }

        /// <summary>
        /// Retrieves a paginated list of customers matching optional search and filter criteria.
        /// </summary>
        /// <param name="filter">Filtering, sorting, and pagination parameters passed via query string.</param>
        /// <param name="cancellationToken">Cancellation token for async execution.</param>
        /// <returns>A paginated result set containing customer summary DTOs and pagination metadata.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(PagedResult<CustomerDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetCustomers([FromQuery] CustomerFilter filter,CancellationToken cancellationToken)
        {
            PagedResult<CustomerDto> result = await _customerQueries.GetCustomersAsync(filter, cancellationToken);

            return Ok(result);
        }


        /// <summary>
        /// Searches Square for existing customers matching contact info to avoid duplicate accounts.
        /// </summary>
        [HttpPost("search-square")]
        [ProducesResponseType(typeof(IEnumerable<SquareCustomerCandidateDto>), StatusCodes.Status200OK)]
        public async Task<IActionResult> SearchSquareCustomers(
            [FromBody] SearchSquareCustomerRequest request,
            CancellationToken cancellationToken)
        {
            var candidates = await _customerService.SearchSquareCustomersAsync(
                request.Email,
                request.PhoneNumber,
                cancellationToken);

            return Ok(candidates);
        }
        // Fix the domain object reference 
        /// <summary>
        /// Provisions a new customer in Square (if required) and creates the local database record.
        /// </summary>
        [HttpPost]
        [ProducesResponseType(typeof(CustomerDto), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> CreateCustomer(
            [FromBody] CreateCustomerRequest request,
            CancellationToken cancellationToken)
        {
            Address? address = null;
            if (!string.IsNullOrWhiteSpace(request.AddressLine1) || !string.IsNullOrWhiteSpace(request.City))
            {
                address = new Address(
                    request.AddressLine1 ?? string.Empty,
                    request.City ?? string.Empty,
                    request.State ?? string.Empty,
                    request.PostalCode ?? string.Empty
                );
            }

            var customerDto = new CustomerDto(
                firstName: request.FirstName,
                lastName: request.LastName,
                email: request.Email,
                phoneNumber: request.PhoneNumber,
                notes: request.Notes,
                address: address,
                squareCustomerId: request.SquareCustomerId
            );

            Guid customerId = await _customerService.CreateCustomerWorkflowAsync(command, cancellationToken);

            CustomerDto? customer = await _customerQueries.GetCustomerByIdAsync(customerId, cancellationToken);

            if (customer is null)
            {
                throw new NotFoundException($"Customer with ID '{customerId}' was not found after creation.");
            }

            return CreatedAtAction(nameof(GetCustomerById), new { id = customerId }, customer);
        }
    }
}
