using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Entities.Identity.Roles
{
    public class Permission
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }
}
