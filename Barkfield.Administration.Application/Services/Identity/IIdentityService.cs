using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public interface IIdentityService
    {
        Task<AuthenticationResult?> LoginAsync(string email, string password);
        Task<Guid?> RegisterAsync(string email, string password, string name);
    }
}
