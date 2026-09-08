using Barkfield.Administration.Application.Services.Identity.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public interface IIdentityService
    {
        Task<AuthenticationResult?> LoginAsync(string email, string password);
        Task LogoutAsync(string? userId, string? refreshToken, CancellationToken cancellationToken);
        Task<Guid?> RegisterAsync(string email, string password, string name);
    }
}
