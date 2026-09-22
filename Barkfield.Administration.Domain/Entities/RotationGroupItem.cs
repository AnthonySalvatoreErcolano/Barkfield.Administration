using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// One product in a rotation's sequence, with the quantity that ships when its turn comes up.
/// </summary>
/// <remarks>
/// Quantity lives here rather than on the group so a 2-bag chicken and a 1-bag lamb can share
/// the same rotation. Mutated only through <see cref="RotationGroup"/>, which owns sequencing.
/// </remarks>
public class RotationGroupItem
{
    public Guid Id { get; private set; }
    public Guid RotationGroupId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>1-based position in the rotation. Kept contiguous by the owning group.</summary>
    public int SequenceOrder { get; private set; }

    public int Quantity { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    private RotationGroupItem() { }

    internal static RotationGroupItem Create(Guid rotationGroupId, Guid productId, int sequenceOrder, int quantity)
    {
        if (rotationGroupId == Guid.Empty)
            throw new DomainException("A rotation item must belong to a valid rotation group.");

        if (productId == Guid.Empty)
            throw new DomainException("A rotation item must reference a valid product.");

        ValidateQuantity(quantity);

        return new RotationGroupItem
        {
            Id = Guid.NewGuid(),
            RotationGroupId = rotationGroupId,
            ProductId = productId,
            SequenceOrder = sequenceOrder,
            Quantity = quantity,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static RotationGroupItem FromDto(
        Guid id,
        Guid rotationGroupId,
        Guid productId,
        int sequenceOrder,
        int quantity,
        DateTime createdAt,
        DateTime? updatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid rotation group item ID.");

        return new RotationGroupItem
        {
            Id = id,
            RotationGroupId = rotationGroupId,
            ProductId = productId,
            SequenceOrder = sequenceOrder,
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

    internal void SetSequenceOrder(int sequenceOrder)
    {
        SequenceOrder = sequenceOrder;
        UpdatedAt = DateTime.UtcNow;
    }

    private static void ValidateQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");
    }
}
