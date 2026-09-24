using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Deliveries;

/// <summary>
/// Rebuilds the <see cref="Delivery"/> aggregate from a detail read.
/// </summary>
/// <remarks>
/// The address, coordinates and window come from the delivery's own snapshotted columns, not from
/// the customer as they stand today — that snapshot is what stops a customer moving house from
/// rewriting where last month's delivery went.
/// </remarks>
public static class DeliveryRehydrator
{
    public static Delivery Rehydrate(DeliveryDetailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var lines = dto.Lines
            .Select(l => DeliveryLine.FromDto(
                l.Id,
                dto.Id,
                l.ProductId,
                l.ProductName,
                l.UnitPrice,
                l.Quantity,
                l.Source,
                l.SourceId,
                l.OrderStatus,
                l.QuantityReceived,
                l.SubstitutedWithProductId,
                l.SubstitutedWithProductName,
                l.SubstitutedWithUnitPrice,
                l.StatusNote,
                l.StatusUpdatedAt))
            .ToList();

        var address = new Address(
            dto.DeliveryStreet,
            dto.DeliveryCity,
            dto.DeliveryState,
            dto.DeliveryZipCode);

        GeoPoint? coordinates = null;

        if (dto.DeliveryLatitude is not null && dto.DeliveryLongitude is not null)
        {
            coordinates = GeoPoint.Create((double)dto.DeliveryLatitude.Value, (double)dto.DeliveryLongitude.Value);
        }

        TimeWindow? requestedWindow = null;

        if (dto.RequestedWindowStart is not null && dto.RequestedWindowEnd is not null)
        {
            requestedWindow = TimeWindow.Create(dto.RequestedWindowStart.Value, dto.RequestedWindowEnd.Value);
        }

        return Delivery.FromDto(
            dto.Id,
            dto.SubscriptionId,
            dto.CustomerId,
            dto.ScheduledFor,
            dto.CompletedAt,
            dto.Status,
            dto.FulfillmentMethod,
            dto.ProcurementStatus,
            dto.HasPaid,
            dto.AstroCompleted,
            dto.ExternalOrderId,
            dto.SentToRoutingAt,
            address,
            coordinates,
            requestedWindow,
            dto.ServiceDurationMinutesOverride,
            dto.Notes,
            dto.FailureReason,
            dto.CreatedAt,
            dto.UpdatedAt,
            lines);
    }
}
