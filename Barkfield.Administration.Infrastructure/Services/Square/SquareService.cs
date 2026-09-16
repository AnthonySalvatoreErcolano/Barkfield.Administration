using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Square;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.Services.Square
{
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
                // Build text filter for Email
                var emailFilter = new CustomerTextFilter.Builder()
                    .Exact(email.Trim())
                    .Build();

                CustomerFilter? filter;

                if (!string.IsNullOrWhiteSpace(phoneNumber))
                {
                    // Build text filter for Phone
                    var phoneFilter = new CustomerTextFilter.Builder()
                        .Exact(phoneNumber.Trim())
                        .Build();

                    filter = new CustomerFilter.Builder()
                        .EmailAddress(emailFilter)
                        .PhoneNumber(phoneFilter)
                        .Build();
                }
                else
                {
                    filter = new CustomerFilter.Builder()
                        .EmailAddress(emailFilter)
                        .Build();
                }

                var query = new CustomerQuery.Builder()
                    .Filter(filter)
                    .Build();

                var searchRequest = new SearchCustomersRequest.Builder()
                    .Query(query)
                    .Limit(10L)
                    .Build();

                SearchCustomersResponse response = await _squareClient.CustomersApi
                    .SearchCustomersAsync(searchRequest, cancellationToken);

                if (response.Errors != null && response.Errors.Any())
                {
                    var errorMessages = string.Join("; ", response.Errors.Select(e => $"{e.Code}: {e.Detail}"));
                    _logger.LogError("Square API returned errors during SearchCustomers: {Errors}", errorMessages);
                    throw new InvalidOperationException($"Square customer search failed: {errorMessages}");
                }

                if (response.Customers == null || !response.Customers.Any())
                {
                    return Enumerable.Empty<SquareCustomerCandidateDto>();
                }

                return response.Customers.Select(c => new SquareCustomerCandidateDto(
                    SquareCustomerId: c.Id,
                    FirstName: c.GivenName ?? string.Empty,
                    LastName: c.FamilyName ?? string.Empty,
                    Email: c.EmailAddress ?? string.Empty,
                    PhoneNumber: c.PhoneNumber
                ));
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "ApiException occurred while querying Square SearchCustomers for {Email}", email);
                throw new InvalidOperationException($"Square API request failed with status code {ex.ResponseCode}", ex);
            }
        }

        /// <summary>
        /// Provisions a new seller customer profile on Square's servers.
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
                // Optional address mapping
                Square.Models.Address? squareAddress = null;
                if (address != null)
                {
                    squareAddress = new Square.Models.Address.Builder()
                        .AddressLine1(address.Line1)
                        .AddressLine2(address.Line2)
                        .Locality(address.City)
                        .AdministrativeDistrictLevel1(address.State)
                        .PostalCode(address.PostalCode)
                        .Country("US") // Adjust or inject default country code as needed
                        .Build();
                }

                // Generate an Idempotency Key to prevent duplicate creates if retried
                string idempotencyKey = Guid.NewGuid().ToString();

                var createRequest = new Square.Models.CreateCustomerRequest.Builder()
                    .IdempotencyKey(idempotencyKey)
                    .GivenName(firstName.Trim())
                    .FamilyName(lastName.Trim())
                    .EmailAddress(email.Trim())
                    .PhoneNumber(phoneNumber?.Trim())
                    .Address(squareAddress)
                    .Note(notes)
                    .Build();

                CreateCustomerResponse response = await _squareClient.CustomersApi
                    .CreateCustomerAsync(createRequest, cancellationToken);

                if (response.Errors != null && response.Errors.Any())
                {
                    var errorMessages = string.Join("; ", response.Errors.Select(e => $"{e.Code}: {e.Detail}"));
                    _logger.LogError("Square API returned errors during CreateCustomer: {Errors}", errorMessages);
                    throw new InvalidOperationException($"Failed to create customer in Square: {errorMessages}");
                }

                if (response.Customer?.Id == null)
                {
                    throw new InvalidOperationException("Square API responded successfully but returned an empty customer ID.");
                }

                return response.Customer.Id;
            }
            catch (ApiException ex)
            {
                _logger.LogError(ex, "ApiException occurred while creating Square customer for {Email}", email);
                throw new InvalidOperationException($"Square API request failed with status code {ex.ResponseCode}", ex);
            }
        }
    }
