using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Microsoft.Extensions.Logging;
using Square;
using Square.Customers;
using System;
using System.Collections.Generic;
using System.Text;
using Address = Barkfield.Administration.Domain.ValueObjects.Address;
using SquareAddress = Square.Address;

namespace Barkfield.Administration.Infrastructure.Services.Square
{
    /// <summary>
    /// Wraps the Square .NET SDK (v47, the generated client) for the customer profile
    /// operations the admin API needs.
    /// </summary>
    public class SquareService : ISquareService
    {
        private readonly ISquareClient _squareClient;
        private readonly ILogger<SquareService> _logger;

        public SquareService(
            ISquareClient squareClient,
            ILogger<SquareService> logger)
        {
            _squareClient = squareClient;
            _logger = logger;
        }

        /// <summary>
        /// Searches Square profiles matching exact email or phone number to prevent duplicates.
        /// </summary>
        public async Task<IEnumerable<SquareCustomerCandidateDto>> SearchCustomersAsync(
            string email,
            string? phoneNumber,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(email));

            try
            {
                var filter = new global::Square.CustomerFilter
                {
                    EmailAddress = new CustomerTextFilter { Exact = email.Trim() }
                };

                if (!string.IsNullOrWhiteSpace(phoneNumber))
                {
                    filter.PhoneNumber = new CustomerTextFilter { Exact = phoneNumber.Trim() };
                }

                var searchRequest = new SearchCustomersRequest
                {
                    Limit = 10L,
                    Query = new CustomerQuery { Filter = filter }
                };

                SearchCustomersResponse response = await _squareClient.Customers
                    .SearchAsync(searchRequest, cancellationToken: cancellationToken);

                if (response.Errors != null && response.Errors.Any())
                {
                    var errorMessages = FormatErrors(response.Errors);
                    _logger.LogError("Square API returned errors during SearchCustomers: {Errors}", errorMessages);
                    throw new InvalidOperationException($"Square customer search failed: {errorMessages}");
                }

                if (response.Customers == null || !response.Customers.Any())
                {
                    return [];
                }

                return response.Customers.Select(c => new SquareCustomerCandidateDto(
                    SquareCustomerId: c.Id ?? string.Empty,
                    FirstName: c.GivenName ?? string.Empty,
                    LastName: c.FamilyName ?? string.Empty,
                    Email: c.EmailAddress ?? string.Empty,
                    PhoneNumber: c.PhoneNumber
                )).ToList();
            }
            catch (SquareApiException ex)
            {
                _logger.LogError(ex, "Square API error while querying SearchCustomers for {Email}", email);
                throw new InvalidOperationException($"Square API request failed with status code {ex.StatusCode}", ex);
            }
        }

        /// <summary>
        /// Provisions a new customer profile on Square and returns the assigned Square customer ID.
        /// </summary>
        public async Task<string> CreateCustomerAsync(
            string firstName,
            string lastName,
            string email,
            string? phoneNumber,
            Address? address,
            string? notes,
            CancellationToken cancellationToken = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(firstName, nameof(firstName));
            ArgumentException.ThrowIfNullOrWhiteSpace(lastName, nameof(lastName));
            ArgumentException.ThrowIfNullOrWhiteSpace(email, nameof(email));

            try
            {
                var createRequest = new CreateCustomerRequest
                {
                    // Idempotency key prevents a duplicate profile if the call is retried.
                    IdempotencyKey = Guid.NewGuid().ToString(),
                    GivenName = firstName.Trim(),
                    FamilyName = lastName.Trim(),
                    EmailAddress = email.Trim(),
                    PhoneNumber = phoneNumber?.Trim(),
                    Address = MapAddress(address),
                    Note = notes
                };

                CreateCustomerResponse response = await _squareClient.Customers
                    .CreateAsync(createRequest, cancellationToken: cancellationToken);

                if (response.Errors != null && response.Errors.Any())
                {
                    var errorMessages = FormatErrors(response.Errors);
                    _logger.LogError("Square API returned errors during CreateCustomer: {Errors}", errorMessages);
                    throw new InvalidOperationException($"Failed to create customer in Square: {errorMessages}");
                }

                if (string.IsNullOrWhiteSpace(response.Customer?.Id))
                {
                    throw new InvalidOperationException("Square API responded successfully but returned an empty customer ID.");
                }

                return response.Customer.Id;
            }
            catch (SquareApiException ex)
            {
                _logger.LogError(ex, "Square API error while creating customer for {Email}", email);
                throw new InvalidOperationException($"Square API request failed with status code {ex.StatusCode}", ex);
            }
        }

        /// <summary>
        /// Pushes local profile changes back to the linked Square customer record.
        /// </summary>
        public async Task UpdateCustomerAsync(UpdateSquareCustomerDto dto, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(dto);
            ArgumentException.ThrowIfNullOrWhiteSpace(dto.SquareCustomerId, nameof(dto.SquareCustomerId));

            try
            {
                var updateRequest = new UpdateCustomerRequest
                {
                    CustomerId = dto.SquareCustomerId,
                    GivenName = dto.FirstName?.Trim(),
                    FamilyName = dto.LastName?.Trim(),
                    EmailAddress = dto.Email?.Trim(),
                    Address = MapAddress(dto.Address),
                    Note = dto.Notes
                };

                UpdateCustomerResponse response = await _squareClient.Customers
                    .UpdateAsync(updateRequest, cancellationToken: cancellationToken);

                if (response.Errors != null && response.Errors.Any())
                {
                    var errorMessages = FormatErrors(response.Errors);
                    _logger.LogError("Square API returned errors during UpdateCustomer: {Errors}", errorMessages);
                    throw new InvalidOperationException($"Failed to update customer in Square: {errorMessages}");
                }
            }
            catch (SquareApiException ex)
            {
                _logger.LogError(ex, "Square API error while updating customer {SquareCustomerId}", dto.SquareCustomerId);
                throw new InvalidOperationException($"Square API request failed with status code {ex.StatusCode}", ex);
            }
        }

        /// <summary>
        /// Maps the domain <see cref="Address"/> value object onto Square's address model.
        /// </summary>
        private static SquareAddress? MapAddress(Address? address)
        {
            if (address is null) return null;

            return new SquareAddress
            {
                AddressLine1 = address.Street,
                Locality = address.City,
                AdministrativeDistrictLevel1 = address.State,
                PostalCode = address.ZipCode,
                Country = global::Square.Country.Us
            };
        }

        private static string FormatErrors(IEnumerable<global::Square.Error> errors) =>
            string.Join("; ", errors.Select(e => $"{e.Code}: {e.Detail}"));
    }
}
