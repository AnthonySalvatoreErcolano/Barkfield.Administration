using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Application.Repositories.Identity.Users;
using Barkfield.Administration.Domain.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services
{
    public class UserService
    {
        private readonly IUserQueries _userQueries;
        private readonly IUserCommands _userCommands;
        private readonly IUserRoleQueries _userRoleQueries;

        public UserService(IUserQueries userQueries, IUserCommands userCommands, IUserRoleQueries userRoleQueries)
        {
            _userQueries = userQueries;
            _userCommands = userCommands;
            _userRoleQueries = userRoleQueries;
        }

        public async Task<User> GetUserWithRoles(Guid userId)
        {
            var user = await _userQueries.GetUserById(userId);

            if (user == null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }

            var userRoles = await _userRoleQueries.GetUserRolesByUserId(userId);

            return new User(user.Id, user.Email, user.Name, userRoles);
        }
    }
}
