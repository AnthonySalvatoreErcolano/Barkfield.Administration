using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Domain.Identity;
using Barkfield.Administration.Domain.Identity.Roles;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace Barkfield.Administration.Application.Services
{
    public class UserService
    {
        private readonly IUserQueries _userQueries;
        private readonly IUserCommands _userCommands;
        private readonly IPasswordHasher _passwordHasher;
        private readonly ITokenGenerator _tokenGenerator;
        public UserService(IUserQueries userQueries, IUserCommands userCommands, IUserRoleQueries userRoleQueries,IPasswordHasher passwordHasher, ITokenGenerator tokenGenerator    )
        {
            _userQueries = userQueries;
            _userCommands = userCommands;
            _passwordHasher = passwordHasher;
            _tokenGenerator = tokenGenerator;
        }

        public async Task<User> GetUserWithRoles(Guid userId, CancellationToken cancellationToken)
        {
            var user = await _userQueries.GetUserAndRolesByUserIdAsync(userId, cancellationToken);

            if (user == null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }


            return new User(user.Id, user.Email, user.Name, userRoles);
        }

        public async Task<Guid> CreateUserAsync(string email, string name, IEnumerable<Guid> roles,string password, CancellationToken cancellationToken)
        {
            string passwordHash = _passwordHasher.HashPassword(password);
            User user = User.Create(name, email, passwordHash, roles);
            var roleParams = user.RoleIds.Select(roleId => new UserRoleDto{ UserId = user.Id,RoleId = roleId });
            await _userCommands.Create(new UserDto { Email = user.Email, Name = user.Name, PasswordHash = passwordHash, IsActive = true, Id = user.Id }, roleParams, cancellationToken);

            return user.Id;
        }

        public async Task UpdateUserAsync( Guid userId, string email,string name,IEnumerable<Guid> roles, CancellationToken cancellationToken)
        {
            UserDetailDto? dto = await _userQueries.GetUserAndRolesByUserIdAsync(userId, cancellationToken);

            if (dto is null)
            {
                throw new NotFoundException($"User with ID '{userId}' was not found.");
            }

            User user = User.FromDto( dto.Id,  dto.Name,  dto.Email, dto.PasswordHash, dto.IsActive,   dto.CreatedAt, dto.UserRoles);
            user.UpdateProfile(name, email);
            user.SyncRoles(roles);
            var roleParams = user.RoleIds.Select(roleId => new UserRoleDto { UserId = user.Id, RoleId = roleId });

            await _userCommands.Update(new UserDto { Email=user.Email, PasswordHash=user.PasswordHash,Name=user.Name,IsActive=user.IsActive},roleParams,cancellationToken);
        }


        public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
        {
            UserDto? dto = await _userQueries.GetByEmailAsync(email, cancellationToken);

            // Prevent email enumeration: return silently if user does not exist
            if (dto is null) return;

            // 1. Generate raw token and computed hash via ITokenHasher
            string rawToken = _tokenGenerator.GenerateRawToken();
            string tokenHash = _tokenGenerator.HashToken(rawToken);
            DateTime expiresAt = DateTime.UtcNow.AddHours(1);


            await _userCommands.SetPasswordResetTokenAsync(command, cancellationToken);

            // 3. Send raw token to user via email infrastructure
            await _emailService.SendPasswordResetEmailAsync(dto.Email, rawToken, cancellationToken);
        }

        public async Task CompletePasswordResetAsync(string email, string rawToken, string newPassword, CancellationToken cancellationToken)
        {
            UserDto? dto = await _userQueries.GetByEmailWithResetTokenAsync(email, cancellationToken);
            if (dto is null || string.IsNullOrWhiteSpace(dto.PasswordResetTokenHash) || !dto.PasswordResetTokenExpiresAt.HasValue)
            {
                throw new DomainException("Invalid password reset request.");
            }

            // 1. Verify expiration window
            if (DateTime.UtcNow > dto.PasswordResetTokenExpiresAt.Value)
            {
                throw new DomainException("The password reset token has expired.");
            }

            // 2. Verify incoming raw token against stored hash using ITokenHasher
            bool isTokenValid = _tokenHasher.VerifyToken(rawToken, dto.PasswordResetTokenHash);
            if (!isTokenValid)
            {
                throw new DomainException("Invalid password reset token.");
            }

            // 3. Hash new password via IPasswordHasher
            string newPasswordHash = _passwordHasher.HashPassword(newPassword);

            // 4. Rehydrate domain aggregate to enforce core domain validation on profile update
            IEnumerable<Guid> roles = await _userRoleQueries.GetUserRolesByUserIdAsync(dto.Id, cancellationToken);
            User user = User.FromDto(dto.Id, dto.Name, dto.Email, newPasswordHash, dto.IsActive, dto.CreatedAt, roles);

            // 5. Persist updated password and clear reset token fields
            var command = new UpdateUserPasswordCommand(user.Id, user.PasswordHash);
            await _userCommands.UpdatePasswordAsync(command, cancellationToken);
        }

    }


}
