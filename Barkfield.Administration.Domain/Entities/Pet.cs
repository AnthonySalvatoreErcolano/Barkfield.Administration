using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A customer's pet, with the allergies that constrain what they can be sent.
/// </summary>
/// <remarks>
/// Allergies are the operationally important part: they are what staff check before a
/// protein goes in a box, which is why they travel with the pet rather than being a
/// separate lookup.
/// </remarks>
public class Pet
{
    private readonly List<Guid> _allergyIds = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }

    public string Name { get; private set; } = string.Empty;
    public DateTime? Birthday { get; private set; }
    public PetType PetType { get; private set; }
    public string? Breed { get; private set; }
    public string? Notes { get; private set; }
    public string? PictureUrl { get; private set; }

    /// <summary>
    /// False once deactivated. Pets are not deleted — most often one leaves the list because
    /// it has died, and the record is worth keeping.
    /// </summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    public IReadOnlyCollection<Guid> AllergyIds => _allergyIds.AsReadOnly();

    private Pet() { }

    public static Pet Create(
        Guid customerId,
        string name,
        PetType petType,
        DateTime? birthday = null,
        string? breed = null,
        string? notes = null,
        string? pictureUrl = null,
        IEnumerable<Guid>? allergyIds = null)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("A pet must belong to a valid customer.");

        ValidateName(name);
        ValidateBirthday(birthday);
        ValidatePetType(petType);

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
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        if (allergyIds is not null)
        {
            pet._allergyIds.AddRange(allergyIds.Distinct().Where(id => id != Guid.Empty));
        }

        return pet;
    }

    public static Pet FromDto(
        Guid id,
        Guid customerId,
        string name,
        PetType petType,
        DateTime? birthday,
        string? breed,
        string? notes,
        string? pictureUrl,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<Guid>? allergyIds = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid pet ID.");

        var pet = new Pet
        {
            Id = id,
            CustomerId = customerId,
            Name = name,
            PetType = petType,
            Birthday = birthday,
            Breed = breed,
            Notes = notes,
            PictureUrl = pictureUrl,
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (allergyIds is not null)
        {
            pet._allergyIds.AddRange(allergyIds.Distinct());
        }

        return pet;
    }

    /// <summary>
    /// Updates the pet's details, running the same validation as creation.
    /// </summary>
    public void Update(
        string name,
        PetType petType,
        DateTime? birthday,
        string? breed,
        string? notes,
        string? pictureUrl)
    {
        ValidateName(name);
        ValidateBirthday(birthday);
        ValidatePetType(petType);

        Name = name.Trim();
        PetType = petType;
        Birthday = birthday?.Date;
        Breed = breed?.Trim();
        Notes = notes?.Trim();
        PictureUrl = pictureUrl?.Trim();
        Touch();
    }

    /// <summary>
    /// Replaces the allergy list wholesale. An empty list is valid — most pets have none.
    /// </summary>
    public void SyncAllergies(IEnumerable<Guid>? allergyIds)
    {
        var ids = allergyIds?.Distinct().ToList() ?? [];

        if (ids.Any(id => id == Guid.Empty))
            throw new DomainException("Invalid allergy ID.");

        _allergyIds.Clear();
        _allergyIds.AddRange(ids);
        Touch();
    }

    public void AddAllergy(Guid allergyId)
    {
        if (allergyId == Guid.Empty)
            throw new DomainException("Invalid allergy ID.");

        if (_allergyIds.Contains(allergyId)) return;

        _allergyIds.Add(allergyId);
        Touch();
    }

    public void RemoveAllergy(Guid allergyId)
    {
        if (_allergyIds.Remove(allergyId))
        {
            Touch();
        }
    }

    public void UpdatePicture(string? pictureUrl)
    {
        PictureUrl = pictureUrl?.Trim();
        Touch();
    }

    public void UpdateNotes(string? notes)
    {
        Notes = notes?.Trim();
        Touch();
    }

    public void Deactivate()
    {
        if (!IsActive) return;

        IsActive = false;
        Touch();
    }

    public void Reactivate()
    {
        if (IsActive) return;

        IsActive = true;
        Touch();
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

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

    private static void ValidatePetType(PetType petType)
    {
        if (!Enum.IsDefined(petType))
            throw new DomainException($"Unknown pet type: {petType}.");
    }
}
