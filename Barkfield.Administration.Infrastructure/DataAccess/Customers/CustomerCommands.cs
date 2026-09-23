using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Customers;

public class CustomerCommands : ICustomerCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public CustomerCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        const string sql = @"
            INSERT INTO dbo.Customers
                (Id, SquareCustomerId, Email, FirstName, LastName, PhoneNumber, Notes,
                 Street, City, State, ZipCode, IsActive, CreatedAt)
            VALUES
                (@Id, @SquareCustomerId, @Email, @FirstName, @LastName, @PhoneNumber, @Notes,
                 @Street, @City, @State, @ZipCode, @IsActive, @CreatedAt);";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(customer), cancellationToken);
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        // SquareCustomerId is deliberately absent: the Square link is managed only by
        // LinkSquareCustomerAsync, so an edit cannot silently repoint a customer.
        const string sql = @"
            UPDATE dbo.Customers
               SET Email       = @Email,
                   FirstName   = @FirstName,
                   LastName    = @LastName,
                   PhoneNumber = @PhoneNumber,
                   Notes       = @Notes,
                   Street      = @Street,
                   City        = @City,
                   State       = @State,
                   ZipCode     = @ZipCode,
                   IsActive    = @IsActive,
                   UpdatedAt   = @UpdatedAt
             WHERE Id = @Id;";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(customer), cancellationToken);
    }

    public async Task LinkSquareCustomerAsync(
        Guid customerId,
        string squareCustomerId,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Customers
               SET SquareCustomerId = @SquareCustomerId,
                   UpdatedAt        = SYSUTCDATETIME()
             WHERE Id = @CustomerId;";

        await _sqlExecutor.ExecuteAsync(
            sql,
            new { CustomerId = customerId, SquareCustomerId = squareCustomerId },
            cancellationToken);
    }

    public async Task<bool> SetActiveAsync(
        Guid customerId,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Customers
               SET IsActive  = @IsActive,
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @CustomerId;";

        int affected = await _sqlExecutor.ExecuteAsync(
            sql,
            new { CustomerId = customerId, IsActive = isActive },
            cancellationToken);

        return affected > 0;
    }

    /// <summary>
    /// Flattens the aggregate into SQL parameters, unpacking the Address value object.
    /// </summary>
    private static object ToParameters(Customer customer) => new
    {
        customer.Id,
        customer.SquareCustomerId,
        customer.Email,
        customer.FirstName,
        customer.LastName,
        customer.PhoneNumber,
        customer.Notes,
        Street = customer.Address?.Street,
        City = customer.Address?.City,
        State = customer.Address?.State,
        ZipCode = customer.Address?.ZipCode,
        customer.IsActive,
        customer.CreatedAt,
        customer.UpdatedAt
    };
}
