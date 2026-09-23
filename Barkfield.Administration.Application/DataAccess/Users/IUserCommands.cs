using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Users;

/// <summary>
/// Writes for the user aggregate.
/// </summary>
/// <remarks>
/// Takes the domain entity, matching the customer slice, so that <see cref="User.Create"/>
/// and its validation sit on the only path into the database. Role assignments travel with
/// the user — they are part of the aggregate, not a separate concern.
/// </remarks>
public interface IUserCommands
{
    /// <summary>Inserts the user and their role assignments in one transaction.</summary>
    Task CreateAsync(User user, CancellationToken cancellationToken = default);

    /// <summary>Updates the profile and replaces the role assignments, in one transaction.</summary>
    Task UpdateAsync(User user, CancellationToken cancellationToken = default);

    Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken = default);

    /// <summary>Soft delete. Returns false if no such user exists.</summary>
    Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default);
}
