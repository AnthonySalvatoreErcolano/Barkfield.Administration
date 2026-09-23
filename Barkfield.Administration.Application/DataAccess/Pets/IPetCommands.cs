using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Pets;

/// <summary>
/// Writes for pets and their allergy links.
/// </summary>
/// <remarks>
/// Takes the domain entity so <see cref="Pet.Create"/> and <see cref="Pet.Update"/> sit on
/// the only path into the database. Allergy links travel with the pet — they are part of
/// the same aggregate and must land in the same transaction.
/// </remarks>
public interface IPetCommands
{
    Task CreateAsync(Pet pet, CancellationToken cancellationToken = default);

    Task UpdateAsync(Pet pet, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. Returns false if no such pet exists.</summary>
    Task<bool> SetActiveAsync(Guid petId, bool isActive, CancellationToken cancellationToken = default);
}
