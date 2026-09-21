using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services
{
    public class UserService
    {
        private readonly IUserQueries _userQueries;
        private readonly IUserCommands _userCommands;
        private readonly IPasswordHasher _passwordHasher;

        public UserService(IUserQueries userQueries, IUserCommands userCommands, IPasswordHasher passwordHasher)
        {
            _userQueries = userQueries;
            _userCommands = userCommands;
            _passwordHasher = passwordHasher;
        }

        public async Task<User> GetUserWithRoles(Guid userId, CancellationToken cancellationToken)
        {
            UserDetailDto? dto = await _userQueries.GetUserAndRolesByUserIdAsync(userId, cancellationToken);

            if (dto is null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }

            return User.FromDto(
                dto.Id,
                dto.Name,
                dto.Email ?? string.Empty,
                dto.PasswordHash ?? string.Empty,
                dto.IsActive,
                dto.CreatedAt,
                dto.UserRoles ?? []);
        }

        public async Task<Guid> CreateUserAsync(string email, string name, IEnumerable<Guid> roles, string password, CancellationToken cancellationToken)
        {
            string passwordHash = _passwordHasher.HashPassword(password);
            User user = User.Create(name, email, passwordHash, roles);
            var roleParams = user.RoleIds.Select(roleId => new UserRoleDto { UserId = user.Id, RoleId = roleId });

            await _userCommands.Create(
                new UserDto
                {
                    Id = user.Id,
                    Email = user.Email!,
                    Name = user.Name,
                    PasswordHash = passwordHash,
                    IsActive = true,
                    CreatedAt = user.CreatedAt
                },
                roleParams,
                cancellationToken);

            return user.Id;
        }

        public async Task UpdateUserAsync(Guid userId, string name, string email, IEnumerable<Guid> roles, CancellationToken cancellationToken)
        {
            UserDetailDto? dto = await _userQueries.GetUserAndRolesByUserIdAsync(userId, cancellationToken);

            if (dto is null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }

            User user = User.FromDto(
                dto.Id,
                dto.Name,
                dto.Email ?? string.Empty,
                dto.PasswordHash ?? string.Empty,
                dto.IsActive,
                dto.CreatedAt,
                dto.UserRoles ?? []);

            user.UpdateProfile(name, email);
            user.SyncRoles(roles);

            var roleParams = user.RoleIds.Select(roleId => new UserRoleDto { UserId = user.Id, RoleId = roleId });

            await _userCommands.Update(
                new UserDto
                {
                    Id = user.Id,
                    Email = user.Email!,
                    Name = user.Name,
                    PasswordHash = user.PasswordHash!,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedAt
                },
                roleParams,
                cancellationToken);
        }
    }
}
