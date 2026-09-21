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
            int totalCount = await _sqlExecutor.QuerySingleAsync<int>(countSql, dynamicParams, cancellationToken);


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
    }
}
