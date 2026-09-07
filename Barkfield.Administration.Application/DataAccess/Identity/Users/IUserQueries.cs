using Barkfield.Administration.Application.DataAccess.Dtos;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Repositories.Identity.Users
{
    public interface IUserQueries
    {
        public Task<UserDto?> GetUserByEmail(string email);
        public Task<UserDto?> GetUserById(Guid userId);

    }
}
