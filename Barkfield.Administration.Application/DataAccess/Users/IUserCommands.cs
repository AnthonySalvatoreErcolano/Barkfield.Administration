using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Domain.Identity;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Users
{
    public interface IUserCommands
    {
        public Task<bool> Create(UserDto user, CancellationToken cancellationToken);
    }
}
