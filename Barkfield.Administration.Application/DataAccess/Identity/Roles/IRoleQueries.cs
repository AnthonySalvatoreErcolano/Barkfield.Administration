namespace Barkfield.Administration.Application.DataAccess.Identity.Roles;

public interface IRoleQueries
{
    Task<IReadOnlyCollection<RoleDto>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns only the ids that exist, so a create or update can reject unknown roles
    /// before hitting a foreign key error.
    /// </summary>
    Task<IReadOnlyCollection<Guid>> GetExistingIdsAsync(IEnumerable<Guid> roleIds, CancellationToken cancellationToken = default);
}
