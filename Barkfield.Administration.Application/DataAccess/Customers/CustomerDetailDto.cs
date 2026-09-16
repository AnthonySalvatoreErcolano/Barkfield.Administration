using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Text;

namespace Barkfield.Administration.Application.DataAccess.Customers
{
    public class CustomerDetailDto
    {
        public Guid Id { get; private set; }
        public string? SquareCustomerId { get; set; }
        public string Email { get; private set; } = string.Empty;
        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        public string? PhoneNumber { get; private set; }
        public string? Notes { get; private set; }
        public Address? Address { get; private set; }
        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
        public IEnumerable<PetDto> Pets { get; private set; } = new List<PetDto>();

    }
}
