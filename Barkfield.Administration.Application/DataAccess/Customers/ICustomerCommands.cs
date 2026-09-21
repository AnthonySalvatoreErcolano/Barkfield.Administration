using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Customers
{
    public interface ICustomerCommands
    {
        public Task<Guid> CreateCustomerAsync(CustomerDto dto, CancellationToken cancellationToken = default);
        public Task Update(Guid customerId, CustomerDto updatedDto, CancellationToken cancellationToken);
    }
}
