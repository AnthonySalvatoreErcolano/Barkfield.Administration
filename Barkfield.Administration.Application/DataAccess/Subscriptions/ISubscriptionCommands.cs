using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Subscriptions;

/// <summary>
/// Writes for the subscription aggregate.
/// </summary>
/// <remarks>
/// <para>
/// There is one write method, and it takes the whole aggregate. Every mutation is
/// load → call a domain method → save, so the domain's invariants sit on the only path in and
/// no SQL statement has to re-implement a rule the entity already enforces.
/// </para>
/// <para>
/// <b>Children are upserted by id, not deleted and reinserted.</b> The pets slice gets away
/// with delete-and-reinsert because <c>PetAllergies</c> is a link table with no identity of its
/// own. Here every child carries state that must survive a save:
/// <c>RotationGroups.RotationPosition</c> is the cursor deciding what the customer receives
/// next, <c>SubscriptionAddOns.ConsumedAt</c> is history, and <c>SubscriptionItems.Id</c> is
/// what a delivery line points back at. Recreating those rows would reset the rotation,
/// re-ship a consumed add-on and orphan the delivery trail.
/// </para>
/// </remarks>
public interface ISubscriptionCommands
{
    Task CreateAsync(Subscription subscription, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the aggregate and everything under it in one transaction.
    /// </summary>
    /// <param name="expectedRevision">
    /// The revision read when this request loaded the subscription. The write is rejected if the
    /// row has moved on since, which turns two requests interleaving into a visible conflict
    /// instead of one silently discarding the other's children.
    /// </param>
    /// <returns>False when the optimistic check failed and nothing was written.</returns>
    Task<bool> SaveAsync(
        Subscription subscription,
        int expectedRevision,
        CancellationToken cancellationToken = default);
}
