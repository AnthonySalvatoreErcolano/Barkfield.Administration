namespace Barkfield.Administration.Application.DataAccess.Pets;

public interface IPetQueries
{
    Task<PetDto?> GetByIdAsync(Guid petId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A customer's pets, with their allergies. Deactivated pets are excluded unless asked for.
    /// </summary>
    Task<IReadOnlyCollection<PetDto>> GetByCustomerIdAsync(Guid customerId, bool includeInactive = false, CancellationToken cancellationToken = default);
}
