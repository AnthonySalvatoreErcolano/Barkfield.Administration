using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A discount staff applied to one delivery.
/// </summary>
/// <remarks>
/// <para>
/// Only <see cref="SquareDiscountId"/> is sent when billing. Square computes the reduction and
/// the total, because Square's catalog is the source of truth for pricing — so nothing here is
/// ever used for arithmetic.
/// </para>
/// <para>
/// The name and rate are snapshotted so a delivery from March still says what discount it was
/// given after somebody edits or deletes that discount in Square. Display only.
/// </para>
/// </remarks>
public class DeliveryDiscount
{
    public Guid DeliveryId { get; private set; }

    /// <summary>Square catalog object id of the discount. The only part that is sent.</summary>
    public string SquareDiscountId { get; private set; } = string.Empty;

    /// <summary>Name as it was when applied. Display only.</summary>
    public string Name { get; private set; } = string.Empty;

    /// <summary>Square's type string, e.g. FIXED_PERCENTAGE. Display only.</summary>
    public string? DiscountType { get; private set; }

    /// <summary>Rate as it was when applied, e.g. 12.00. Display only.</summary>
    public decimal? Percentage { get; private set; }

    /// <summary>Fixed amount off as it was when applied. Display only.</summary>
    public decimal? AmountOff { get; private set; }

    public DateTime CreatedAt { get; private set; }

    private DeliveryDiscount() { }

    internal static DeliveryDiscount Create(
        Guid deliveryId,
        string squareDiscountId,
        string name,
        string? discountType,
        decimal? percentage,
        decimal? amountOff)
    {
        if (deliveryId == Guid.Empty)
            throw new DomainException("A delivery discount must belong to a valid delivery.");

        if (string.IsNullOrWhiteSpace(squareDiscountId))
            throw new DomainException("A Square discount ID is required.");

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("A discount name is required.");

        return new DeliveryDiscount
        {
            DeliveryId = deliveryId,
            SquareDiscountId = squareDiscountId.Trim(),
            Name = name.Trim(),
            DiscountType = discountType?.Trim(),
            Percentage = percentage,
            AmountOff = amountOff,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static DeliveryDiscount FromDto(
        Guid deliveryId,
        string squareDiscountId,
        string name,
        string? discountType,
        decimal? percentage,
        decimal? amountOff,
        DateTime createdAt) => new()
        {
            DeliveryId = deliveryId,
            SquareDiscountId = squareDiscountId,
            Name = name,
            DiscountType = discountType,
            Percentage = percentage,
            AmountOff = amountOff,
            CreatedAt = createdAt
        };

    /// <summary>Human label for a screen or a printed sheet.</summary>
    public override string ToString() => Percentage is not null
        ? $"{Name} ({Percentage:0.##}%)"
        : AmountOff is not null ? $"{Name} ({AmountOff:C} off)" : Name;
}
