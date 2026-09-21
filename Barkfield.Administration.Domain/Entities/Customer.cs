using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Barkfield.Administration.Domain.Entities
{
    public class Customer
    {
        // Aggregate Identifier
        public Guid Id { get; private set; }

        public string? SquareCustomerId { get; set; }

        // Domain Properties (Read-only outside the domain model)
        public string Email { get; private set; } = string.Empty;
        public string FirstName { get; private set; } = string.Empty;
        public string LastName { get; private set; } = string.Empty;
        public string? PhoneNumber { get; private set; }
        public string? Notes { get; private set; }
        public Address? Address { get; private set; }

        public string FullName => $"{FirstName} {LastName}".Trim();

        public DateTime CreatedAt { get; private set; }
        public DateTime? UpdatedAt { get; private set; }
        private Customer() { }

        /// <summary>
        /// Factory method for creating a new Customer aggregate root.
        /// Ensures all invariants are satisfied upon instantiation.
        /// </summary>
        public static Customer Create(
            string firstName,
            string lastName,
            string email,
            string? phoneNumber = null,
            Address? address = null,
            string? notes = null,
            string? squareCustomerId = null)
        {
            ValidateName(firstName, nameof(firstName));
            ValidateName(lastName, nameof(lastName));
            ValidateEmail(email);

            string? cleanPhoneNumber = NormalizePhoneNumber(phoneNumber);

            return new Customer
            {
                Id = Guid.NewGuid(),
                FirstName = firstName.Trim(),
                LastName = lastName.Trim(),
                Email = email.Trim().ToLowerInvariant(),
                PhoneNumber = cleanPhoneNumber,
                Address = address,
                Notes = notes?.Trim(),
                SquareCustomerId = squareCustomerId?.Trim(),
                CreatedAt = DateTime.UtcNow
            };
        }


        public void Update( string firstName, string lastName, string email,string? phoneNumber,string? notes,
         Address? address,string? squareCustomerId)
        {
            FirstName = firstName;
            LastName = lastName;
            Email = email;
            PhoneNumber = phoneNumber;
            Notes = notes;
            Address = address;
            SquareCustomerId = squareCustomerId;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Links or updates the external Square POS customer identifier.
        /// </summary>
        public void LinkSquareCustomer(string squareCustomerId)
        {
            if (string.IsNullOrWhiteSpace(squareCustomerId))
                throw new DomainException("Square Customer ID cannot be empty.");

            SquareCustomerId = squareCustomerId.Trim();
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Updates the physical or billing address for the customer.
        /// </summary>
        public void UpdateAddress(Address? newAddress)
        {
            Address = newAddress;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Updates administrative notes regarding the customer account.
        /// </summary>
        public void UpdateNotes(string? notes)
        {
            Notes = notes?.Trim();
            UpdatedAt = DateTime.UtcNow;
        }


        private static void ValidateName(string name, string paramName)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new DomainException($"{paramName} is required and cannot be empty.");

            if (name.Trim().Length > 100)
                throw new DomainException($"{paramName} cannot exceed 100 characters.");
        }

        private static void ValidateEmail(string email)
        {
            if (string.IsNullOrWhiteSpace(email))
                throw new DomainException("Email address is required.");

            if (!Regex.IsMatch(email.Trim(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.IgnoreCase))
                throw new DomainException("Invalid email address format.");
        }

        private static string? NormalizePhoneNumber(string? phoneNumber)
        {
            if (string.IsNullOrWhiteSpace(phoneNumber))
                return null;

            string digitsOnly = Regex.Replace(phoneNumber, @"[^\d]", "");

            if (digitsOnly.Length < 10 || digitsOnly.Length > 15)
                throw new DomainException("Phone number must contain between 10 and 15 digits.");

            return digitsOnly;
        }
    }
}
