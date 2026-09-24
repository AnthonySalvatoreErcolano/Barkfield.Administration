using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.Services.Deliveries.Models;

namespace Barkfield.Administration.Application.DataAccess.Deliveries;

public interface IDeliveryQueries
{
    Task<PagedResult<DeliveryListItemDto>> SearchAsync(
        DeliveryFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a delivery with its lines, the customer's live contact details, and the address
    /// snapshotted onto it.
    /// </summary>
    /// <remarks>
    /// Backs reads and writes both. The service rehydrates the aggregate from this, so a mutation
    /// pays for one trip to the database before its save and cannot see a different shape from
    /// the read path.
    /// </remarks>
    Task<DeliveryDetailDto?> GetByIdAsync(Guid deliveryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// The printable sheet for a date range: one entry per delivery, with what to pull for it.
    /// </summary>
    /// <remarks>
    /// A range rather than a single date because the prep day covers more than one delivery day.
    /// Cancelled deliveries are excluded; shorted lines are kept so nothing looks lost.
    /// </remarks>
    Task<IReadOnlyCollection<DeliverySheetDto>> GetSheetAsync(
        DateTime from,
        DateTime to,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscriptions generation would pick up for a date, with every reason it might not.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Deliberately "due <b>or overdue</b>" — next delivery on or before the date. A day nobody
    /// generates would otherwise drop those subscriptions into a gap they never come out of.
    /// </para>
    /// <para>
    /// Includes paused subscriptions whose return date has arrived, because generation is what
    /// brings them back.
    /// </para>
    /// </remarks>
    Task<IReadOnlyCollection<DueSubscriptionDto>> GetDueSubscriptionsAsync(
        DateTime deliveryDate,
        CancellationToken cancellationToken = default);
}
