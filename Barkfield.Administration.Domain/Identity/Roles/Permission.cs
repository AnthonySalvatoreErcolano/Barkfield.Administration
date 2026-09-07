using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Identity.Roles
{
    public class Permission
    {
        public int Id { get; set; }

        // Naming convention standard: "users:create", "billing:view"
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
