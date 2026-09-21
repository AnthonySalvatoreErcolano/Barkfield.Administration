using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles
{
    public interface IUserRoleCommands
    {
        public Task Create(IEnumerable<UserRoleDto> userRoles, CancellationToken cancellationToken);
    }
}
