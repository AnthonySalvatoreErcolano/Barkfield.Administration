using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Customers;

/// <summary>
/// Writes for the customer aggregate.
/// </summary>
/// <remarks>
/// These take the domain entity rather than a DTO, so that <see cref="Customer.Create"/>
/// and <see cref="Customer.Update"/> — and the validation they carry — are on the only
/// path into the database. Mapping entity to SQL parameters is the data layer's job.
/// </remarks>
public interface ICustomerCommands
{
    Task CreateAsync(Customer customer, CancellationToken cancellationToken = default);

    Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default);

    /// <summary>Attaches or replaces the Square profile link.</summary>
    Task LinkSquareCustomerAsync(Guid customerId, string squareCustomerId, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. Returns false if no such customer exists.</summary>
    Task<bool> SetActiveAsync(Guid customerId, bool isActive, CancellationToken cancellationToken = default);
}
