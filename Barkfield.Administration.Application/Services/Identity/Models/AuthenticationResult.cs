using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity.Models
{
    public record AuthenticationResult(string AccessToken, string RefreshToken, DateTime RefreshTokenExpiresAt, string email, Guid userId);
 
}
