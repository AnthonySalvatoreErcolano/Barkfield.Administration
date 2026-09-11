using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities.Identity.Roles
{
    public class RolePermission
    {

        public Guid RolerId { get; set; }
        public Guid PermissionId { get; set; }
    }
}
