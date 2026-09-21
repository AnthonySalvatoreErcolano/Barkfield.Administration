using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
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
        private readonly ICustomerQueries _customerQueries;


        public CustomerService(ISquareService squareService, ICustomerCommands customerCommands, ICustomerQueries customerQueries)
        {
            _squareService = squareService;
            _customerCommands = customerCommands;
            _customerQueries = customerQueries;
        }
        public async Task<IEnumerable<SquareCustomerCandidateDto>> SearchSquareCustomersAsync(string email,     string? phoneNumber,CancellationToken cancellationToken = default)
        {
            return await _squareService.SearchCustomersAsync(email, phoneNumber, cancellationToken);
        }

        public async Task<Guid> CreateCustomerAsync(CustomerDto dto,CancellationToken cancellationToken = default)
        {
            string? finalSquareId = dto.SquareCustomerId;

            // 1. Provision new customer in Square if no existing Square profile was selected
            if (string.IsNullOrWhiteSpace(finalSquareId))
            {
               
                finalSquareId = await _squareService.CreateCustomerAsync(
                    dto.FirstName,
                    dto.LastName,
                    dto.Email,
                    dto.PhoneNumber,
                    dto.Address,
                    dto.Notes,
                    cancellationToken
                );
            }
            dto.SquareCustomerId = finalSquareId;

            return await _customerCommands.CreateCustomerAsync(dto, cancellationToken);
        }

        public async Task UpdateCustomerAsync(Guid customerId,CustomerDto updatedDto,CancellationToken cancellationToken = default)
        {
            // 1. Fetch existing customer
            CustomerDetailDto? existingCustomer = await _customerQueries.GetCustomerByIdAsync(customerId, cancellationToken);
            if (existingCustomer is null)
            {
                throw new NotFoundException($"Customer with ID '{customerId}' was not found.");
            }

            // 2. Sync changes with Square if integrated
            if (!string.IsNullOrWhiteSpace(updatedDto.SquareCustomerId))
            {
                await _squareService.UpdateCustomerAsync(new UpdateSquareCustomerDto { Address=updatedDto.Address, Email=updatedDto.Email, FirstName=updatedDto.FirstName, LastName=updatedDto.LastName, Notes=updatedDto.Notes, SquareCustomerId=updatedDto.SquareCustomerId }, cancellationToken);
            }

            // 3. Persist updated customer
            await _customerCommands.Update(customerId, updatedDto, cancellationToken);
        }
    }
}
