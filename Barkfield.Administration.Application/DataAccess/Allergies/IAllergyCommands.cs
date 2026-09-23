namespace Barkfield.Administration.Application.DataAccess.Allergies;

public interface IAllergyCommands
{
    /// <summary>
    /// Adds an allergy to the shared list, or returns the id of the existing one with that
    /// name. Staff will meet allergies the seed list does not cover.
    /// </summary>
    Task<Guid> GetOrCreateAsync(string allergyName, CancellationToken cancellationToken = default);
}
