using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Sqaure.Dtos
{
    public record SquareCustomerCandidateDto(
     string SquareCustomerId,
     string FirstName,
     string LastName,
     string Email,
     string? PhoneNumber
 );
}
