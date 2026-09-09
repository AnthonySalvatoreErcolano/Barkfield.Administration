using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public interface IPasswordHasher
    {
        public string HashPassword(string password);
        public bool VerifyPassword(string password, string hashedPassword);
    }
}
