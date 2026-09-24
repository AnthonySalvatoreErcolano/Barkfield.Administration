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
                 Street, City, State, ZipCode,
                 AccessNotes, ServiceDurationMinutes, PreferredWindowStart, PreferredWindowEnd,
                 Latitude, Longitude, IsActive, CreatedAt)
            VALUES
                (@Id, @SquareCustomerId, @Email, @FirstName, @LastName, @PhoneNumber, @Notes,
                 @Street, @City, @State, @ZipCode,
                 @AccessNotes, @ServiceDurationMinutes, @PreferredWindowStart, @PreferredWindowEnd,
                 @Latitude, @Longitude, @IsActive, @CreatedAt);";

        await _sqlExecutor.ExecuteAsync(sql, ToParameters(customer), cancellationToken);
    }

    public async Task UpdateAsync(Customer customer, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(customer);

        // SquareCustomerId is deliberately absent: the Square link is managed only by
        // LinkSquareCustomerAsync, so an edit cannot silently repoint a customer.
        //
        // The routing columns ARE written here even though the edit form does not collect them,
        // because the entity was loaded carrying them and writes back what it holds. Leaving
        // them out of this statement would be safe; leaving them out of the SELECT that loads
        // the customer would not, which is why both sides list them.
        const string sql = @"
            UPDATE dbo.Customers
               SET Email                  = @Email,
                   FirstName              = @FirstName,
                   LastName               = @LastName,
                   PhoneNumber            = @PhoneNumber,
                   Notes                  = @Notes,
                   Street                 = @Street,
                   City                   = @City,
                   State                  = @State,
                   ZipCode                = @ZipCode,
                   AccessNotes            = @AccessNotes,
                   ServiceDurationMinutes = @ServiceDurationMinutes,
                   PreferredWindowStart   = @PreferredWindowStart,
                   PreferredWindowEnd     = @PreferredWindowEnd,
                   Latitude               = @Latitude,
                   Longitude              = @Longitude,
                   IsActive               = @IsActive,
                   UpdatedAt              = @UpdatedAt
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
    /// Flattens the aggregate into SQL parameters, unpacking the Address, TimeWindow and
    /// GeoPoint value objects.
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
        customer.AccessNotes,
        customer.ServiceDurationMinutes,
        PreferredWindowStart = customer.PreferredWindow?.Start,
        PreferredWindowEnd = customer.PreferredWindow?.End,

        // GeoPoint holds doubles; the column is DECIMAL(9,6). Cast here rather than letting
        // Dapper infer a float parameter against a decimal column.
        Latitude = (decimal?)customer.Coordinates?.Latitude,
        Longitude = (decimal?)customer.Coordinates?.Longitude,
        customer.IsActive,
        customer.CreatedAt,
        customer.UpdatedAt
    };
}
