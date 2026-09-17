using System;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Customers
{
    public class CustomerDto
    {
        public CustomerDto(string firstName,string lastName,string email,string? phoneNumber = null,       string? notes = null,Address? address = null,string? squareCustomerId = null)
        {
            Id = Guid.NewGuid();
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            PhoneNumber = phoneNumber;
            Notes = notes;
            Address = address;
            SquareCustomerId = squareCustomerId;
            CreatedAt = DateTime.UtcNow;
        }

        public CustomerDto( string? squareCustomerId,string email,string firstName,string lastName,string? phoneNumber,string? notes,Address? address,DateTime createdAt,DateTime? updatedAt)
        {
            SquareCustomerId = squareCustomerId;
            Email = email;
            FirstName = firstName;
            LastName = lastName;
            PhoneNumber = phoneNumber;
            Notes = notes;
            Address = address;
            CreatedAt = createdAt;
            UpdatedAt = updatedAt;
        }

        public CustomerDto()
        {
        }

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
    }
}