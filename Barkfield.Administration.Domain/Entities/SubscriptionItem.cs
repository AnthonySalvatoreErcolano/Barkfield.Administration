using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A static line on a subscription: this product, this quantity, every delivery.
/// The "5x Dog Food" case.
/// </summary>
/// <remarks>
/// Mutated only through <see cref="Subscription"/>, which owns the no-duplicate-product invariant.
/// </remarks>
public class SubscriptionItem
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private SubscriptionItem() { }

    internal static SubscriptionItem Create(Guid subscriptionId, Guid productId, int quantity)
    {
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A subscription item must belong to a valid subscription.");

        if (productId == Guid.Empty)
            throw new DomainException("A subscription item must reference a valid product.");

        ValidateQuantity(quantity);

        return new SubscriptionItem
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static SubscriptionItem FromDto(
        Guid id,
        Guid subscriptionId,
        Guid productId,
        int quantity,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid subscription item ID.");

        return new SubscriptionItem
        {
            Id = id,
            SubscriptionId = subscriptionId,
            ProductId = productId,
            Quantity = quantity,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };
    }

    internal void ChangeQuantity(int quantity)
    {
        ValidateQuantity(quantity);

        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    internal void IncreaseQuantity(int by)
    {
        ValidateQuantity(by);

        Quantity += by;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
    }
}
