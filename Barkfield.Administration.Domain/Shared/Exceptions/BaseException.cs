using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Exceptions
{
    public abstract class BaseException(string message) : Exception(message)
    {
    }
}
