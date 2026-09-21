using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.Services.Sqaure.Dtos
{
    public class UpdateSquareCustomerDto
    {

        public string SquareCustomerId { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public Address Address { get; set; }

        public string Notes { get; set; }
    }
}
