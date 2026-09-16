using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles;
using Barkfield.Administration.Domain.Entities.Identity.Tokens;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Users
{
    public interface IUserCommands
    {
        public Task<bool> Create(UserDto user, IEnumerable<UserRoleDto> roles, CancellationToken cancellationToken);

        public Task<bool> Update(UserDto user, IEnumerable<UserRoleDto> roles, CancellationToken cancellationToken);

        public Task<bool> CreateResetToken(PasswordResetTokenDto resetToken, CancellationToken cancellationToken);
    }
}
