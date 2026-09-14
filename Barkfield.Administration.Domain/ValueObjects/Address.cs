using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Domain.ValueObjects
{
    public record Address(string Street,
      string City,
      string State,
      string ZipCode)
    {
    }
}
