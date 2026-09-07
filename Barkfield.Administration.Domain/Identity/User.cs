using Barkfield.Administration.Domain.Identity.Roles;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Identity
{
    public class User
    {
        public Guid Id { get; set; }
        public string Name { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }

        public IEnumerable<Guid> UserRoles { get; set; }

        public User()
        {
                
        }

        public User(Guid userId, string email, string name, IEnumerable<Guid> userRoles)
        {
            Id= userId;
            Email= email;
            Name= name;
            UserRoles = userRoles;
            
        }
    }
}
