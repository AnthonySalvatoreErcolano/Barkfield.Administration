using Barkfield.Administration.Application.Services.Identity.Models;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens
{
    public interface IRefreshTokenQueries
    {
        Task<RefreshToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken);
    }
}
