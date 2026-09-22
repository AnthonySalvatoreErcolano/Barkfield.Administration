using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A one-off product attached to the next delivery only, which drops off automatically once
/// that delivery completes.
/// </summary>
/// <remarks>
/// There is deliberately no target date. An add-on applies to whichever delivery happens next,
/// so rescheduling the subscription cannot orphan it. Consumed add-ons are retained rather than
/// deleted, so the delivery log can still show what shipped and why.
/// </remarks>
public class SubscriptionAddOn
{
    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }

    /// <summary>Optional staff note, e.g. "customer asked for a sample".</summary>
    public string? Note { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>Null while the add-on is still waiting to ship.</summary>
    public DateTime? ConsumedAt { get; private set; }

    /// <summary>True while this add-on still belongs on the next delivery.</summary>
    public bool IsPending => ConsumedAt is null;

    private SubscriptionAddOn() { }

    internal static SubscriptionAddOn Create(Guid subscriptionId, Guid productId, int quantity, string? note = null)
    {
        if (subscriptionId == Guid.Empty)
            throw new DomainException("An add-on must belong to a valid subscription.");

        if (productId == Guid.Empty)
            throw new DomainException("An add-on must reference a valid product.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        return new SubscriptionAddOn
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            ProductId = productId,
            Quantity = quantity,
            Note = note?.Trim(),
            CreatedAt = DateTime.UtcNow
        };
    }

    public static SubscriptionAddOn FromDto(
        Guid id,
        Guid subscriptionId,
        Guid productId,
        int quantity,
        string? note,
        DateTime createdAt,
        DateTime? consumedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid add-on ID.");

        return new SubscriptionAddOn
        {
            Id = id,
            SubscriptionId = subscriptionId,
            ProductId = productId,
            Quantity = quantity,
            Note = note,
            CreatedAt = createdAt,
            ConsumedAt = consumedAt
        };
    }

    internal void ChangeQuantity(int quantity)
    {
        if (!IsPending)
            throw new DomainException("A consumed add-on cannot be changed.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        Quantity = quantity;
    }

    /// <summary>
    /// Marks the add-on as shipped so it drops off subsequent deliveries.
    /// </summary>
    internal void MarkConsumed(DateTime consumedOn)
    {
        if (!IsPending)
            throw new DomainException("This add-on has already been consumed.");

        ConsumedAt = consumedOn;
    }
}
