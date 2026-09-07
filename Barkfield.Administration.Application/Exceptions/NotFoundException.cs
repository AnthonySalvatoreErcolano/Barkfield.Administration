using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Exceptions
{
    public class NotFoundException(string message) : BaseException(message)
    {
    }
}
