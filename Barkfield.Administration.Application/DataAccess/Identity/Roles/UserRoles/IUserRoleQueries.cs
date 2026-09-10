using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Repositories.Identity.Roles.UserRoles
{
    public interface IUserRoleQueries
    {
        public Task<IEnumerable<Guid>> GetUserRolesByUserId(Guid userId, CancellationToken cancellationToken);
    }
}
