using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Exceptions
{
    public class DatabaseException(string message, Exception innerException)
     : Exception(message, innerException);
}
