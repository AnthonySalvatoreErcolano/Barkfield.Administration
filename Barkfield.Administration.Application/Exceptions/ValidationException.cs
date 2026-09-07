using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Exceptions
{
    public class ValidationException(string message) : BaseException(message)
    {
    }
}
