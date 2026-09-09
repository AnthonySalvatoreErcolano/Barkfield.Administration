using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Domain.Identity;
using Barkfield.Administration.Domain.Identity.Roles;
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
        private readonly IUserRoleCommands _userRoleCommands;
        private readonly IPasswordHasher _passwordHasher;
        public UserService(IUserQueries userQueries, IUserCommands userCommands, IUserRoleQueries userRoleQueries,IPasswordHasher passwordHasher, IUserRoleCommands userRoleCommands    )
        {
            _userQueries = userQueries;
            _userCommands = userCommands;
            _userRoleQueries = userRoleQueries;
            _passwordHasher = passwordHasher;
            _userRoleCommands = userRoleCommands;
        }

        public async Task<User> GetUserWithRoles(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _userQueries.GetUserByIdAsync(userId, cancellationToken);

            if (user == null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }

            var userRoles = await _userRoleQueries.GetUserRolesByUserId(userId);

            return new User(user.Id, user.Email, user.Name, userRoles);
        }

        public async Task<Guid> CreateUserAsync(string email, string name, IEnumerable<Guid> roles,string password, CancellationToken cancellationToken)
        {
            string passwordHash = _passwordHasher.HashPassword(password);
            User user = User.Create(name, email, passwordHash, roles);
            
            await _userCommands.Create(new UserDto { Email = user.Email, Name = user.Name, PasswordHash = passwordHash, IsActive = true, Id = user.Id }, cancellationToken);

            var roleParams = user.RoleIds.Select(roleId => new UserRoleDto{ UserId = user.Id,RoleId = roleId });

            await _userRoleCommands.Create(roleParams, cancellationToken);

            return user.Id;
        }

    }
}
