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
        /// <summary>Fallback stop duration when none is set, in minutes.</summary>
        public const int DefaultServiceDurationMinutes = 5;

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

        /// <summary>
        /// Set when a routing provider returns coordinates for the address. Not required —
        /// Routific geocodes from the address string.
        /// </summary>
        public GeoPoint? Coordinates { get; private set; }

        /// <summary>Driver-facing instructions — gate codes, "leave at side door", "dog in yard".</summary>
        public string? AccessNotes { get; private set; }

        /// <summary>How long the stop takes. Sent as the routing order's service duration.</summary>
        public int ServiceDurationMinutes { get; private set; } = DefaultServiceDurationMinutes;

        /// <summary>The customer's preferred delivery window, if any.</summary>
        public TimeWindow? PreferredWindow { get; private set; }

        public string FullName => $"{FirstName} {LastName}".Trim();

        /// <summary>
        /// True when there is enough here to route a stop to. Pickup customers legitimately
        /// have no address, so this is checked when a local delivery is scheduled rather than
        /// enforced on the customer itself.
        /// </summary>
        public bool CanReceiveLocalDelivery => Address is not null;

        /// <summary>
        /// False once deactivated. Customers are never hard-deleted — delivery history
        /// references them, and that history must outlive the relationship.
        /// </summary>
        public bool IsActive { get; private set; }

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
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Rehydrates a customer loaded from the database.
        /// </summary>
        public static Customer FromDto(
            Guid id,
            string firstName,
            string lastName,
            string email,
            string? phoneNumber,
            string? notes,
            Address? address,
            string? squareCustomerId,
            bool isActive,
            DateTime createdAt,
            DateTime? updatedAt,
            GeoPoint? coordinates = null,
            string? accessNotes = null,
            int serviceDurationMinutes = DefaultServiceDurationMinutes,
            TimeWindow? preferredWindow = null)
        {
            if (id == Guid.Empty)
                throw new DomainException("Invalid customer ID.");

            return new Customer
            {
                Id = id,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                PhoneNumber = phoneNumber,
                Notes = notes,
                Address = address,
                Coordinates = coordinates,
                AccessNotes = accessNotes,
                ServiceDurationMinutes = serviceDurationMinutes,
                PreferredWindow = preferredWindow,
                SquareCustomerId = squareCustomerId,
                IsActive = isActive,
                CreatedAt = createdAt,
                UpdatedAt = updatedAt
            };
        }

        /// <summary>
        /// Applies an edit, running the same validation as creation.
        /// </summary>
        /// <remarks>
        /// The Square link is deliberately not settable here — it is managed by
        /// <see cref="LinkSquareCustomer"/> so that an edit form cannot silently repoint
        /// a customer at a different Square profile.
        /// </remarks>
        public void Update(
            string firstName,
            string lastName,
            string email,
            string? phoneNumber,
            string? notes,
            Address? address)
        {
            ValidateName(firstName, nameof(firstName));
            ValidateName(lastName, nameof(lastName));
            ValidateEmail(email);

            FirstName = firstName.Trim();
            LastName = lastName.Trim();
            Email = email.Trim().ToLowerInvariant();
            PhoneNumber = NormalizePhoneNumber(phoneNumber);
            Notes = notes?.Trim();
            Address = address;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Deactivates the customer. Their delivery history is retained.
        /// </summary>
        public void Deactivate()
        {
            if (!IsActive) return;

            IsActive = false;
            UpdatedAt = DateTime.UtcNow;
        }

        public void Reactivate()
        {
            if (IsActive) return;

            IsActive = true;
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
        /// <remarks>
        /// Coordinates are cleared, because they describe the address that was just replaced.
        /// Routing will geocode the new one on its next pass; a stale coordinate would send a
        /// driver confidently to the wrong house.
        /// </remarks>
        public void UpdateAddress(Address? newAddress)
        {
            Address = newAddress;
            Coordinates = null;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Updates the driver-facing detail for this customer's stop: access notes, how long
        /// the stop takes, and the window they prefer.
        /// </summary>
        /// <remarks>
        /// Separate from <see cref="Update"/> because this is operational information the
        /// dispatch side maintains, not part of the customer's identity. It also means the
        /// customer edit form cannot wipe a gate code by omitting it.
        /// </remarks>
        public void UpdateDeliveryDetails(
            string? accessNotes,
            int? serviceDurationMinutes = null,
            TimeWindow? preferredWindow = null)
        {
            AccessNotes = string.IsNullOrWhiteSpace(accessNotes) ? null : accessNotes.Trim();
            ServiceDurationMinutes = ValidateServiceDuration(serviceDurationMinutes ?? ServiceDurationMinutes);
            PreferredWindow = preferredWindow;
            UpdatedAt = DateTime.UtcNow;
        }

        /// <summary>
        /// Records coordinates handed back by a routing provider.
        /// </summary>
        public void SetCoordinates(GeoPoint? coordinates)
        {
            Coordinates = coordinates;
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

        private static int ValidateServiceDuration(int minutes)
        {
            if (minutes is < 0 or > 480)
                throw new DomainException("Service duration must be between 0 and 480 minutes.");

            return minutes;
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
