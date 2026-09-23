using Barkfield.Administration.Application.Common;

namespace Barkfield.Administration.Application.DataAccess.Customers;

public interface ICustomerQueries
{
    /// <summary>Paged, searchable, sortable list for the dashboard.</summary>
    Task<PagedResult<CustomerListItemDto>> SearchAsync(CustomerFilter filter, CancellationToken cancellationToken = default);

    /// <summary>A single customer with their pets. Null if no such customer exists.</summary>
    Task<CustomerDetailDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Exact email lookup, including deactivated customers.</summary>
    Task<CustomerDetailDto?> GetByEmailAsync(string email, CancellationToken cancellationToken = default);

    /// <summary>
    /// True when another customer already uses this email. Used to reject duplicates
    /// before a create or update.
    /// </summary>
    Task<bool> EmailExistsAsync(string email, Guid? excludingCustomerId = null, CancellationToken cancellationToken = default);
}
