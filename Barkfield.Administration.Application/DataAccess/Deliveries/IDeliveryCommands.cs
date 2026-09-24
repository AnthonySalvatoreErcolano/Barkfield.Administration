using Barkfield.Administration.Domain.Entities;

namespace Barkfield.Administration.Application.DataAccess.Deliveries;

/// <summary>
/// Writes for the delivery aggregate.
/// </summary>
/// <remarks>
/// One write method taking the whole aggregate, as with subscriptions, and for the same reason:
/// every mutation is load, call a domain method, save, so the invariants sit on the only path in.
///
/// Lines are upserted by id. Each one carries procurement state — its order status, how many units
/// were physically received, and the substitution snapshot — and recreating those rows would throw
/// away a morning's work in the stockroom.
/// </remarks>
public interface IDeliveryCommands
{
    /// <summary>Inserts a new delivery and its lines in one transaction.</summary>
    Task CreateAsync(Delivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the aggregate and its lines in one transaction.
    /// </summary>
    /// <param name="expectedRevision">
    /// The revision read when this request loaded the delivery. Rejected if the row has moved on,
    /// which turns two staff working the same delivery into a visible conflict rather than one
    /// quietly discarding the other's line changes.
    /// </param>
    /// <returns>False when the optimistic check failed and nothing was written.</returns>
    Task<bool> SaveAsync(
        Delivery delivery,
        int expectedRevision,
        CancellationToken cancellationToken = default);
}
