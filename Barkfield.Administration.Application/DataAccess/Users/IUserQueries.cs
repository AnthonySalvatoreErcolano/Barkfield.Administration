using Barkfield.Administration.Application.DataAccess.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Users
{
    public interface IUserQueries
    {
        public Task<UserDto?> GetUserByEmailAsync(string email);
        public Task<UserDto?> GetUserByIdAsync(Guid userId, CancellationToken cancellationToken);

    }
}
