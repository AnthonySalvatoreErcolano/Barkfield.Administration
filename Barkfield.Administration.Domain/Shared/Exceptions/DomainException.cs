using Barkfield.Administration.Application.Exceptions;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.Shared.Exceptions
{
    public class DomainException(string message) : BaseException(message)
    {
    }
}
