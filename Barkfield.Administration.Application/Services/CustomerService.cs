using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services
{
    public class CustomerService
    {
        private readonly ISquareService _squareService;
        private readonly ICustomerCommands _customerCommands;


        public CustomerService(ISquareService squareService, ICustomerCommands customerCommands)
        {
            _squareService = squareService;
            _customerCommands = customerCommands;
        }
        public async Task<IEnumerable<SquareCustomerCandidateDto>> SearchSquareCustomersAsync(
            string email,
            string? phoneNumber,
            CancellationToken cancellationToken = default)
        {
            return await _squareService.SearchCustomersAsync(email, phoneNumber, cancellationToken);
        }

        public async Task<Guid> CreateCustomerWorkflowAsync(
            CustomerDto dto,
            CancellationToken cancellationToken = default)
        {
            string? finalSquareId = dto.SquareCustomerId;

            // 1. Provision new customer in Square if no existing Square profile was selected
            if (string.IsNullOrWhiteSpace(finalSquareId))
            {
                Address? address = null;
                if (!string.IsNullOrWhiteSpace(command.AddressLine1) || !string.IsNullOrWhiteSpace(command.City))
                {
                    address = Address.Create(
                        command.AddressLine1,
                        command.AddressLine2,
                        command.City,
                        command.State,
                        command.PostalCode
                    );
                }

                finalSquareId = await _squareService.CreateCustomerAsync(
                    command.FirstName,
                    command.LastName,
                    command.Email,
                    command.PhoneNumber,
                    address,
                    command.Notes,
                    cancellationToken
                );
            }

            // 2. Pass final command payload containing guaranteed Square ID to persistence command
            var commandWithSquareId = command with { SquareCustomerId = finalSquareId };

            return await _customerCommands.CreateCustomerAsync(commandWithSquareId, cancellationToken);
        }
    }
}
