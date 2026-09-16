using Barkfield.Administration.Application.Common;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Customers
{
    public interface ICustomerQueries
    {
        Task<PagedResult<CustomerDto>> GetCustomersAsync(CustomerFilter filter, CancellationToken cancellationToken = default);

        Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid id,CancellationToken cancellationToken = default);
    }
}
