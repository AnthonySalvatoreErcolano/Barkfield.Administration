using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// An ordered set of products where exactly one ships per delivery, advancing each cycle —
/// the "rotating proteins" case.
/// </summary>
/// <remarks>
/// <para>
/// The cursor is <see cref="RotationPosition"/>, a counter that only ever increases. The product
/// up next is <c>ItemsInSequence[RotationPosition % Count]</c>. Using a modulo of a monotonic
/// counter rather than a stored index means the cursor can never fall out of range when staff
/// add or remove products mid-rotation, and the number of completed cycles stays derivable.
/// </para>
/// <para>
/// When an item is removed, the position is re-pointed so the product that was already scheduled
/// stays scheduled — otherwise editing the group would silently change what the customer receives.
/// </para>
/// </remarks>
public class RotationGroup
{
    private readonly List<RotationGroupItem> _items = [];

    public Guid Id { get; private set; }
    public Guid SubscriptionId { get; private set; }
    public string Name { get; private set; } = string.Empty;

    /// <summary>Monotonic cursor. Resolved against the item count via modulo; never decreases below zero.</summary>
    public int RotationPosition { get; private set; }

    /// <summary>A paused group contributes nothing to a delivery but keeps its contents and position.</summary>
    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Items in rotation order.</summary>
    public IReadOnlyList<RotationGroupItem> Items =>
        _items.OrderBy(i => i.SequenceOrder).ToList();

    /// <summary>Number of complete passes through the rotation so far.</summary>
    public int CompletedCycles => _items.Count == 0 ? 0 : RotationPosition / _items.Count;

    /// <summary>The product that ships on the next delivery, or null if the group is empty.</summary>
    public RotationGroupItem? CurrentItem
    {
        get
        {
            if (_items.Count == 0) return null;

            var ordered = Items;
            return ordered[RotationPosition % ordered.Count];
        }
    }

    private RotationGroup() { }

    internal static RotationGroup Create(Guid subscriptionId, string name)
    {
        if (subscriptionId == Guid.Empty)
            throw new DomainException("A rotation group must belong to a valid subscription.");

        ValidateName(name);

        return new RotationGroup
        {
            Id = Guid.NewGuid(),
            SubscriptionId = subscriptionId,
            Name = name.Trim(),
            RotationPosition = 0,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static RotationGroup FromDto(
        Guid id,
        Guid subscriptionId,
        string name,
        int rotationPosition,
        bool isActive,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<RotationGroupItem>? items = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid rotation group ID.");

        var group = new RotationGroup
        {
            Id = id,
            SubscriptionId = subscriptionId,
            Name = name,
            RotationPosition = Math.Max(rotationPosition, 0),
            IsActive = isActive,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (items is not null)
        {
            group._items.AddRange(items);
        }

        return group;
    }

    public void Rename(string name)
    {
        ValidateName(name);

        Name = name.Trim();
        Touch();
    }

    /// <summary>
    /// Appends a product to the end of the rotation sequence.
    /// </summary>
    public RotationGroupItem AddItem(Guid productId, int quantity)
    {
        if (_items.Any(i => i.ProductId == productId))
            throw new DomainException("That product is already in this rotation group. Adjust its quantity instead.");

        int nextSequence = _items.Count == 0 ? 1 : _items.Max(i => i.SequenceOrder) + 1;
        var item = RotationGroupItem.Create(Id, productId, nextSequence, quantity);

        _items.Add(item);
        Touch();

        return item;
    }

    /// <summary>
    /// Removes a product from the rotation, keeping the currently-scheduled product scheduled
    /// where possible.
    /// </summary>
    public void RemoveItem(Guid rotationGroupItemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == rotationGroupItemId);
        if (item is null) return;

        Guid? scheduledProductId = CurrentItem?.ProductId;

        _items.Remove(item);
        Resequence();

        // If the product that was up next is still in the rotation, keep pointing at it.
        if (scheduledProductId is not null && scheduledProductId != item.ProductId)
        {
            RepointTo(scheduledProductId.Value);
        }

        Touch();
    }

    public void ChangeItemQuantity(Guid rotationGroupItemId, int quantity)
    {
        var item = _items.FirstOrDefault(i => i.Id == rotationGroupItemId)
            ?? throw new DomainException($"Rotation item '{rotationGroupItemId}' was not found in this group.");

        item.ChangeQuantity(quantity);
        Touch();
    }

    /// <summary>
    /// Replaces the rotation order. The supplied ids must be exactly the group's current items.
    /// The currently-scheduled product stays scheduled.
    /// </summary>
    public void Reorder(IEnumerable<Guid> rotationGroupItemIdsInOrder)
    {
        ArgumentNullException.ThrowIfNull(rotationGroupItemIdsInOrder);

        var ordered = rotationGroupItemIdsInOrder.ToList();

        if (ordered.Count != _items.Count || ordered.Distinct().Count() != ordered.Count
            || ordered.Any(id => _items.All(i => i.Id != id)))
        {
            throw new DomainException("Reorder must list every item in this rotation group exactly once.");
        }

        Guid? scheduledProductId = CurrentItem?.ProductId;

        for (int i = 0; i < ordered.Count; i++)
        {
            _items.First(x => x.Id == ordered[i]).SetSequenceOrder(i + 1);
        }

        if (scheduledProductId is not null)
        {
            RepointTo(scheduledProductId.Value);
        }

        Touch();
    }

    /// <summary>
    /// Moves the cursor on by one. Called when a delivery completes, never when one is skipped.
    /// </summary>
    internal void Advance()
    {
        if (_items.Count == 0) return;

        RotationPosition++;
        Touch();
    }

    /// <summary>
    /// Forces a specific product to be the next one out, e.g. "give them lamb this time".
    /// </summary>
    public void JumpTo(Guid rotationGroupItemId)
    {
        var item = _items.FirstOrDefault(i => i.Id == rotationGroupItemId)
            ?? throw new DomainException($"Rotation item '{rotationGroupItemId}' was not found in this group.");

        RepointTo(item.ProductId);
        Touch();
    }

    /// <summary>
    /// The next <paramref name="count"/> products in rotation order, for previewing upcoming
    /// deliveries in the admin UI. Wraps around as many times as needed.
    /// </summary>
    public IReadOnlyList<RotationGroupItem> PeekUpcoming(int count)
    {
        if (count <= 0 || _items.Count == 0) return [];

        var ordered = Items;

        return Enumerable.Range(0, count)
            .Select(offset => ordered[(RotationPosition + offset) % ordered.Count])
            .ToList();
    }

    public void Pause()
    {
        if (!IsActive) return;

        IsActive = false;
        Touch();
    }

    public void Resume()
    {
        if (IsActive) return;

        IsActive = true;
        Touch();
    }

    /// <summary>
    /// Moves the cursor to the given product without rewinding past completed cycles.
    /// </summary>
    private void RepointTo(Guid productId)
    {
        if (_items.Count == 0) return;

        var ordered = Items;
        int index = ordered.ToList().FindIndex(i => i.ProductId == productId);
        if (index < 0) return;

        RotationPosition = RotationPosition - (RotationPosition % ordered.Count) + index;
    }

    /// <summary>
    /// Renumbers sequence orders to 1..n so removals never leave gaps.
    /// </summary>
    private void Resequence()
    {
        var ordered = _items.OrderBy(i => i.SequenceOrder).ToList();

        for (int i = 0; i < ordered.Count; i++)
        {
            ordered[i].SetSequenceOrder(i + 1);
        }
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Rotation group name is required and cannot be empty.");

        if (name.Trim().Length > 100)
            throw new DomainException("Rotation group name cannot exceed 100 characters.");
    }
}
