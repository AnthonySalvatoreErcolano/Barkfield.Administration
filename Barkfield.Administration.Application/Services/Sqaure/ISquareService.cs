using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Sqaure
{
    public interface ISquareService
    {
        /// <summary>
        /// Calls Square's SearchCustomers endpoint filtered by email and/or phone number.
        /// </summary>
        Task<IEnumerable<SquareCustomerCandidateDto>> SearchCustomersAsync( string email,string? phoneNumber, CancellationToken cancellationToken = default);

        /// <summary>
        /// Calls Square's CreateCustomer endpoint to provision a brand-new seller profile.
        /// Returns the assigned Square Customer ID.
        /// </summary>
        Task<string> CreateCustomerAsync( string firstName, string lastName,string email,string? phoneNumber, Address? address,string? notes,CancellationToken cancellationToken = default);

        Task UpdateCustomerAsync(UpdateSquareCustomerDto dto, CancellationToken cancellationToken);
    }

}
