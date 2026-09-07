using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Identity.Roles
{
    internal class Role
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }

        public ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
    }
}
