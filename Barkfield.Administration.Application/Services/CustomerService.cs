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
    }
}
