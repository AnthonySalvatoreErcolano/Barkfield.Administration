using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Exceptions
{
    public class ExternalServiceException(string message, Exception inner) : Exception(message, inner)
    {
    }
}
