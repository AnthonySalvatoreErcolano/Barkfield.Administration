using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.Services;

/// <summary>
/// Pet records and their allergies.
/// </summary>
public class PetService
{
    private readonly IPetQueries _petQueries;
    private readonly IPetCommands _petCommands;
    private readonly IAllergyQueries _allergyQueries;
    private readonly IAllergyCommands _allergyCommands;
    private readonly ICustomerQueries _customerQueries;

    public PetService(
        IPetQueries petQueries,
        IPetCommands petCommands,
        IAllergyQueries allergyQueries,
        IAllergyCommands allergyCommands,
        ICustomerQueries customerQueries)
    {
        _petQueries = petQueries;
        _petCommands = petCommands;
        _allergyQueries = allergyQueries;
        _allergyCommands = allergyCommands;
        _customerQueries = customerQueries;
    }

    public async Task<Guid> CreatePetAsync(
        Guid customerId,
        string name,
        PetType petType,
        DateTime? birthday,
        string? breed,
        string? notes,
        string? pictureUrl,
        IEnumerable<Guid>? allergyIds,
        CancellationToken cancellationToken = default)
    {
        // Confirms the owner exists before writing, so a bad customer id fails with a clear
        // message rather than a foreign key violation.
        _ = await _customerQueries.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer with ID '{customerId}' was not found.");

        await EnsureAllergiesExistAsync(allergyIds, cancellationToken);

        Pet pet = Pet.Create(customerId, name, petType, birthday, breed, notes, pictureUrl, allergyIds);

        await _petCommands.CreateAsync(pet, cancellationToken);

        return pet.Id;
    }

    public async Task UpdatePetAsync(
        Guid petId,
        string name,
        PetType petType,
        DateTime? birthday,
        string? breed,
        string? notes,
        string? pictureUrl,
        IEnumerable<Guid>? allergyIds,
        CancellationToken cancellationToken = default)
    {
        Pet pet = await LoadAsync(petId, cancellationToken);

        await EnsureAllergiesExistAsync(allergyIds, cancellationToken);

        pet.Update(name, petType, birthday, breed, notes, pictureUrl);
        pet.SyncAllergies(allergyIds);

        await _petCommands.UpdateAsync(pet, cancellationToken);
    }

    /// <summary>
    /// Deactivates a pet. The record is kept — most often a pet leaves the list because it
    /// has died, and the customer's history is worth preserving.
    /// </summary>
    public async Task DeactivatePetAsync(Guid petId, CancellationToken cancellationToken = default)
    {
        if (!await _petCommands.SetActiveAsync(petId, false, cancellationToken))
        {
            throw new NotFoundException($"Pet with ID '{petId}' was not found.");
        }
    }

    public async Task ReactivatePetAsync(Guid petId, CancellationToken cancellationToken = default)
    {
        if (!await _petCommands.SetActiveAsync(petId, true, cancellationToken))
        {
            throw new NotFoundException($"Pet with ID '{petId}' was not found.");
        }
    }

    /// <summary>
    /// Adds an allergy to the shared list, or returns the existing one with that name.
    /// Staff will meet allergies the seeded list does not cover.
    /// </summary>
    public async Task<Guid> AddAllergyToCatalogAsync(string allergyName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(allergyName))
        {
            throw new ValidationException("An allergy name is required.");
        }

        if (allergyName.Trim().Length > 100)
        {
            throw new ValidationException("Allergy name cannot exceed 100 characters.");
        }

        return await _allergyCommands.GetOrCreateAsync(allergyName, cancellationToken);
    }

    private async Task EnsureAllergiesExistAsync(IEnumerable<Guid>? allergyIds, CancellationToken cancellationToken)
    {
        var requested = allergyIds?.Distinct().ToList() ?? [];
        if (requested.Count == 0) return;

        var existing = await _allergyQueries.GetExistingIdsAsync(requested, cancellationToken);
        var missing = requested.Except(existing).ToList();

        if (missing.Count > 0)
        {
            throw new ValidationException($"Unknown allergy id(s): {string.Join(", ", missing)}.");
        }
    }

    private async Task<Pet> LoadAsync(Guid petId, CancellationToken cancellationToken)
    {
        PetDto dto = await _petQueries.GetByIdAsync(petId, cancellationToken)
            ?? throw new NotFoundException($"Pet with ID '{petId}' was not found.");

        return Pet.FromDto(
            dto.Id,
            dto.CustomerId,
            dto.Name,
            dto.PetType,
            dto.Birthday,
            dto.Breed,
            dto.Notes,
            dto.PictureUrl,
            dto.IsActive,
            dto.CreatedAt,
            dto.UpdatedAt,
            dto.AllergyIds);
    }
}
