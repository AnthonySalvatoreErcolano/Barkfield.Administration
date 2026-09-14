using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Barkfield.Administration.Domain.Entities;

public class Pet
{
    private readonly List<PetAllergy> _allergies = [];

    // Aggregate Identifiers
    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }

    // Domain Properties
    public string Name { get; private set; } = string.Empty;
    public DateTime? Birthday { get; private set; }
    public PetType PetType { get; private set; }
    public string? Breed { get; private set; }
    public string? Notes { get; private set; }
    public string? PictureUrl { get; private set; }

    // Encapsulated Collection Read-Only Access
    public IReadOnlyCollection<PetAllergy> Allergies => _allergies.AsReadOnly();

    // Audit Metadata
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    // Private constructor enforces factory usage
    private Pet() { }

    /// <summary>
    /// Factory method for creating a new Pet aggregate entity.
    /// Ensures valid ownership, valid name, and prevents future birthdays.
    /// </summary>
    public static Pet Create(
        Guid customerId,
        string name,
        PetType petType,
        DateTime? birthday = null,
        string? breed = null,
        string? notes = null,
        string? pictureUrl = null,
        IEnumerable<PetAllergy>? allergies = null)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("Pet must belong to a valid Customer (CustomerId cannot be empty).");

        ValidateName(name);
        ValidateBirthday(birthday);

        var pet = new Pet
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Name = name.Trim(),
            PetType = petType,
            Birthday = birthday?.Date,
            Breed = breed?.Trim(),
            Notes = notes?.Trim(),
            PictureUrl = pictureUrl?.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        if (allergies is not null)
        {
            foreach (var allergy in allergies)
            {
                pet.AddAllergy(allergy);
            }
        }

        return pet;
    }

    // --- Domain Behaviors & Mutations ---

    /// <summary>
    /// Updates the core profile details of the pet.
    /// </summary>
    public void UpdateProfile(string name, PetType petType, string? breed, DateTime? birthday)
    {
        ValidateName(name);
        ValidateBirthday(birthday);

        Name = name.Trim();
        PetType = petType;
        Breed = breed?.Trim();
        Birthday = birthday?.Date;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Adds a new allergy to the pet, preventing duplicates.
    /// </summary>
    public void AddAllergy(PetAllergy allergy)
    {
        ArgumentNullException.ThrowIfNull(allergy);

        // Prevent duplicate allergy entries
        if (_allergies.Any(a => a.Name.Equals(allergy.Name, StringComparison.OrdinalIgnoreCase)))
            return;

        _allergies.Add(allergy);
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Removes an allergy by name.
    /// </summary>
    public void RemoveAllergy(string allergyName)
    {
        if (string.IsNullOrWhiteSpace(allergyName)) return;

        int removedCount = _allergies.RemoveAll(a => a.Name.Equals(allergyName.Trim(), StringComparison.OrdinalIgnoreCase));
        if (removedCount > 0)
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Updates or replaces the pet's photo URL.
    /// </summary>
    public void UpdatePicture(string? pictureUrl)
    {
        PictureUrl = pictureUrl?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Updates administrative or care notes for the pet.
    /// </summary>
    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    // --- Invariant Validations ---

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Pet name is required and cannot be empty.");

        if (name.Trim().Length > 50)
            throw new DomainException("Pet name cannot exceed 50 characters.");
    }

    private static void ValidateBirthday(DateTime? birthday)
    {
        if (birthday.HasValue && birthday.Value.Date > DateTime.UtcNow.Date)
            throw new DomainException("Pet birthday cannot be in the future.");
    }
}