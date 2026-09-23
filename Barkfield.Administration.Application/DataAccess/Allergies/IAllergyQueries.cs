namespace Barkfield.Administration.Application.DataAccess.Allergies;

public interface IAllergyQueries
{
    Task<IReadOnlyCollection<AllergyDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the ids that exist, so a pet write can reject unknown allergies with a
    /// clear message rather than a foreign key violation.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetExistingIdsAsync(IEnumerable<Guid> allergyIds, CancellationToken cancellationToken = default);
}
