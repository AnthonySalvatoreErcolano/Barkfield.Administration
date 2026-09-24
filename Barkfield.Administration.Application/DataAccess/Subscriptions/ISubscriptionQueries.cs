using Barkfield.Administration.Application.Common;

namespace Barkfield.Administration.Application.DataAccess.Subscriptions;

public interface ISubscriptionQueries
{
    Task<PagedResult<SubscriptionListItemDto>> SearchAsync(
        SubscriptionFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a subscription with every static item, rotation group, rotation item and pending
    /// add-on, in one round trip.
    /// </summary>
    /// <remarks>
    /// This backs both reads and writes. The service rehydrates the aggregate from the result
    /// rather than running a second leaner query, so "load a subscription" has one definition
    /// and the write path can never see a different shape from the read path.
    /// </remarks>
    Task<SubscriptionDetailDto?> GetByIdAsync(Guid subscriptionId, CancellationToken cancellationToken = default);

    /// <summary>A customer's subscriptions. Several is normal.</summary>
    Task<IReadOnlyCollection<SubscriptionListItemDto>> GetByCustomerIdAsync(
        Guid customerId,
        bool includeCanceled = false,
        CancellationToken cancellationToken = default);
}
