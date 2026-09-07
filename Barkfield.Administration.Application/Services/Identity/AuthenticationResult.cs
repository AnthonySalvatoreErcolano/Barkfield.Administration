using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Identity
{
    public record AuthenticationResult(string token, string email, Guid userId);
 
}
