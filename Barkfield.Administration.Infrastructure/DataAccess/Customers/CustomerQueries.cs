using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Dapper;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Customers
{
    public class CustomerQueries:ICustomerQueries
    {
        private readonly ISqlExecutor _sqlExecutor;

        public CustomerQueries(ISqlExecutor sqlExecutor)
        {
            _sqlExecutor = sqlExecutor;
        }

        public async Task<PagedResult<CustomerDto>> GetCustomersAsync(CustomerFilter filter,CancellationToken cancellationToken = default)
        {
            var builder = new StringBuilder(@"FROM Customers c WHERE 1=1 ");

            var dynamicParams = new DynamicParameters();

            if (!string.IsNullOrWhiteSpace(filter.SearchTerm))
            {
                builder.Append(" AND (c.FirstName LIKE @SearchTerm OR c.LastName LIKE @SearchTerm OR c.Email LIKE @SearchTerm)");
                dynamicParams.Add("SearchTerm", $"%{filter.SearchTerm.Trim()}%");
            }

            if (!string.IsNullOrWhiteSpace(filter.Email))
            {
                builder.Append(" AND c.Email = @Email");
                dynamicParams.Add("Email", filter.Email.Trim().ToLowerInvariant());
            }

            if (filter.HasSquareAccount.HasValue)
            {
                builder.Append(filter.HasSquareAccount.Value
                    ? " AND c.SquareCustomerId IS NOT NULL"
                    : " AND c.SquareCustomerId IS NULL");
            }

            string countSql = $"SELECT COUNT(1) {builder}";
            int totalCount = await _sqlExecutor.ExecuteAsync(countSql, dynamicParams, cancellationToken);


            builder.Append(" ORDER BY c.CreatedAt DESC OFFSET @Skip ROWS FETCH NEXT @PageSize ROWS ONLY;");
            dynamicParams.Add("Skip", filter.Skip);
            dynamicParams.Add("PageSize", filter.PageSize);

            string dataSql = $@"
                                SELECT 
                                    c.Id, 
                                    c.FirstName, 
                                    c.LastName, 
                                    c.Email, 
                                    c.PhoneNumber, 
                                    c.SquareCustomerId, 
                                    c.CreatedAt 
                                {builder}";

            IEnumerable<CustomerDto> items = await _sqlExecutor.QueryAsync<CustomerDto>(dataSql, dynamicParams, cancellationToken);

            return new PagedResult<CustomerDto>(items.ToList(), totalCount, filter.PageNumber, filter.PageSize);
        }

        public Task<CustomerDetailDto?> GetCustomerByIdAsync(Guid id, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public async Task<Guid> CreateCustomerAsync(
        CreateCustomerCommand command,
        CancellationToken cancellationToken = default)
        {
            // 1. Build optional Address Value Object
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

            // 2. Instantiate Customer Aggregate Root (enforces invariants & validations)
            Customer customer = Customer.Create(
                command.FirstName,
                command.LastName,
                command.Email,
                command.PhoneNumber,
                address,
                command.Notes,
                command.SquareCustomerId
            );

            // 3. Map Domain Aggregate -> Persistence Parameters for SQL Server
            var customerParams = new
            {
                customer.Id,
                customer.FirstName,
                customer.LastName,
                customer.Email,
                customer.PhoneNumber,
                customer.SquareCustomerId,
                customer.Notes,
                AddressLine1 = customer.Address?.Line1,
                AddressLine2 = customer.Address?.Line2,
                City = customer.Address?.City,
                State = customer.Address?.State,
                PostalCode = customer.Address?.PostalCode,
                customer.CreatedAt
            };

            const string insertSql = @"
            INSERT INTO Customers (
                Id, FirstName, LastName, Email, PhoneNumber, SquareCustomerId, Notes,
                AddressLine1, AddressLine2, City, State, PostalCode, CreatedAt
            )
            VALUES (
                @Id, @FirstName, @LastName, @Email, @PhoneNumber, @SquareCustomerId, @Notes,
                @AddressLine1, @AddressLine2, @City, @State, @PostalCode, @CreatedAt
            );";

            await _sqlExecutor.ExecuteAsync(insertSql, customerParams, cancellationToken);

            return customer.Id;
        }
    }
}
