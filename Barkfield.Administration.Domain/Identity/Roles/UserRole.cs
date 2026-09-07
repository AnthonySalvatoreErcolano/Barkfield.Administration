using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Identity.Roles
{
    //
    public class UserRole
    {
        public Guid UserId { get; set; }
        public Guid RoleId { get; set; }
    }
}
