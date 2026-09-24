using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Subscriptions;

/// <summary>
/// Rebuilds the <see cref="Subscription"/> aggregate from a detail read.
/// </summary>
/// <remarks>
/// Shared rather than private to one service, because the deliveries slice loads subscriptions
/// too — generation builds their manifests and completing a delivery advances them. One definition
/// means the two cannot disagree about how an aggregate comes back from the database.
/// </remarks>
public static class SubscriptionRehydrator
{
    public static Subscription Rehydrate(SubscriptionDetailDto dto)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var items = dto.Items
            .Select(i => SubscriptionItem.FromDto(i.Id, dto.Id, i.ProductId, i.Quantity, i.CreatedAt, i.UpdatedAt))
            .ToList();

        var groups = dto.RotationGroups
            .Select(g => RotationGroup.FromDto(
                g.Id,
                dto.Id,
                g.Name,
                g.RotationPosition,
                g.IsActive,
                g.CreatedAt,
                g.UpdatedAt,
                g.Items.Select(i => RotationGroupItem.FromDto(
                    i.Id, g.Id, i.ProductId, i.SequenceOrder, i.Quantity, i.CreatedAt, i.UpdatedAt))))
            .ToList();

        var addOns = dto.PendingAddOns
            .Select(a => SubscriptionAddOn.FromDto(
                a.Id, dto.Id, a.ProductId, a.Quantity, a.Note, a.CreatedAt, a.ConsumedAt))
            .ToList();

        return Subscription.FromDto(
            dto.Id,
            dto.CustomerId,
            dto.Name,
            dto.Status,
            OrderFrequency.Every(dto.FrequencyInterval, dto.FrequencyUnit),
            dto.FulfillmentMethod,
            dto.NextDeliveryDate,
            dto.LastDeliveryDate,
            dto.SignUpDate,
            dto.PausedUntil,
            dto.CreatedAt,
            dto.UpdatedAt,
            items,
            groups,
            addOns);
    }
}
