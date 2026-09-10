using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Infrastructure.DataAccess.Users
{
    public class UserQueries : IUserQueries
    {
        public Task<UserDetailDto?> GetUserAndRolesByUserIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<UserDto?> GetUserByEmailAsync(string email, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }
    }
}
