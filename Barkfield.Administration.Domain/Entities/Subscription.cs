using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// A customer's recurring auto-ship order: what ships, how often, and when next.
/// </summary>
/// <remarks>
/// <para>
/// Three kinds of thing can ship, in any combination:
/// <see cref="Items"/> (static, every cycle), <see cref="RotationGroups"/> (one pick per cycle,
/// advancing), and <see cref="AddOns"/> (next delivery only). A subscription may have any number
/// of each, including none.
/// </para>
/// <para>
/// <see cref="BuildNextDeliveryManifest"/> resolves all three into the flat list that packing
/// slips, routes and carts are built from. <see cref="CompleteDelivery"/> is the single operation
/// that advances rotations, expires add-ons and rolls the dates.
/// </para>
/// <para>
/// Deliveries are a separate aggregate (<see cref="Delivery"/>) because they accumulate without
/// bound; this aggregate holds only the next and last dates.
/// </para>
/// </remarks>
public class Subscription
{
    private readonly List<SubscriptionItem> _items = [];
    private readonly List<RotationGroup> _rotationGroups = [];
    private readonly List<SubscriptionAddOn> _addOns = [];

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public SubscriptionStatus Status { get; private set; }
    public OrderFrequency Frequency { get; private set; } = null!;
    public FulfillmentMethod FulfillmentMethod { get; private set; }

    public DateTime NextDeliveryDate { get; private set; }
    public DateTime? LastDeliveryDate { get; private set; }
    public DateTime SignUpDate { get; private set; }

    public DateTime CreatedAt { get; private set; }
    public DateTime? UpdatedAt { get; private set; }

    /// <summary>Static lines that ship on every delivery.</summary>
    public IReadOnlyCollection<SubscriptionItem> Items => _items.AsReadOnly();

    /// <summary>Rotations, each contributing its current pick to every delivery.</summary>
    public IReadOnlyCollection<RotationGroup> RotationGroups => _rotationGroups.AsReadOnly();

    /// <summary>Every add-on ever attached, consumed or not.</summary>
    public IReadOnlyCollection<SubscriptionAddOn> AddOns => _addOns.AsReadOnly();

    /// <summary>Add-ons still waiting to ship on the next delivery.</summary>
    public IReadOnlyCollection<SubscriptionAddOn> PendingAddOns =>
        _addOns.Where(a => a.IsPending).ToList();

    /// <summary>True when the subscription has nothing recurring to ship.</summary>
    public bool HasNothingScheduled =>
        _items.Count == 0 && !_rotationGroups.Any(g => g.IsActive);

    private Subscription() { }

    public static Subscription Create(
        Guid customerId,
        OrderFrequency frequency,
        DateTime firstDeliveryDate,
        FulfillmentMethod fulfillmentMethod = FulfillmentMethod.LocalDelivery)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("A valid CustomerId is required.");

        ArgumentNullException.ThrowIfNull(frequency);

        if (firstDeliveryDate.Date < DateTime.UtcNow.Date)
            throw new DomainException("First delivery date cannot be in the past.");

        return new Subscription
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = SubscriptionStatus.NewSignUp,
            Frequency = frequency,
            FulfillmentMethod = fulfillmentMethod,
            NextDeliveryDate = firstDeliveryDate.Date,
            SignUpDate = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow
        };
    }

    public static Subscription FromDto(
        Guid id,
        Guid customerId,
        SubscriptionStatus status,
        OrderFrequency frequency,
        FulfillmentMethod fulfillmentMethod,
        DateTime nextDeliveryDate,
        DateTime? lastDeliveryDate,
        DateTime signUpDate,
        DateTime createdAt,
        DateTime? updatedAt,
        IEnumerable<SubscriptionItem>? items = null,
        IEnumerable<RotationGroup>? rotationGroups = null,
        IEnumerable<SubscriptionAddOn>? addOns = null)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid subscription ID.");

        ArgumentNullException.ThrowIfNull(frequency);

        var subscription = new Subscription
        {
            Id = id,
            CustomerId = customerId,
            Status = status,
            Frequency = frequency,
            FulfillmentMethod = fulfillmentMethod,
            NextDeliveryDate = nextDeliveryDate,
            LastDeliveryDate = lastDeliveryDate,
            SignUpDate = signUpDate,
            CreatedAt = createdAt,
            UpdatedAt = updatedAt
        };

        if (items is not null) subscription._items.AddRange(items);
        if (rotationGroups is not null) subscription._rotationGroups.AddRange(rotationGroups);
        if (addOns is not null) subscription._addOns.AddRange(addOns);

        return subscription;
    }

    // --- Static items ------------------------------------------------------

    /// <summary>
    /// Adds a product that ships every cycle. Adding a product already on the subscription
    /// raises its quantity rather than creating a duplicate line.
    /// </summary>
    public SubscriptionItem AddItem(Guid productId, int quantity)
    {
        EnsureEditable();

        var existing = _items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is not null)
        {
            existing.IncreaseQuantity(quantity);
            Touch();
            return existing;
        }

        var item = SubscriptionItem.Create(Id, productId, quantity);
        _items.Add(item);
        Touch();

        return item;
    }

    public void ChangeItemQuantity(Guid productId, int quantity)
    {
        EnsureEditable();

        var item = _items.FirstOrDefault(i => i.ProductId == productId)
            ?? throw new DomainException($"Product '{productId}' is not on this subscription.");

        item.ChangeQuantity(quantity);
        Touch();
    }

    public void RemoveItem(Guid productId)
    {
        EnsureEditable();

        if (_items.RemoveAll(i => i.ProductId == productId) > 0)
        {
            Touch();
        }
    }

    // --- Rotation groups ---------------------------------------------------

    public RotationGroup AddRotationGroup(string name)
    {
        EnsureEditable();

        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Rotation group name is required and cannot be empty.");

        if (_rotationGroups.Any(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            throw new DomainException($"This subscription already has a rotation group named '{name.Trim()}'.");

        var group = RotationGroup.Create(Id, name);
        _rotationGroups.Add(group);
        Touch();

        return group;
    }

    public RotationGroup GetRotationGroup(Guid rotationGroupId) =>
        _rotationGroups.FirstOrDefault(g => g.Id == rotationGroupId)
            ?? throw new DomainException($"Rotation group '{rotationGroupId}' was not found on this subscription.");

    public void RemoveRotationGroup(Guid rotationGroupId)
    {
        EnsureEditable();

        if (_rotationGroups.RemoveAll(g => g.Id == rotationGroupId) > 0)
        {
            Touch();
        }
    }

    // --- Add-ons -----------------------------------------------------------

    /// <summary>
    /// Attaches a product to the next delivery only. It is consumed automatically when that
    /// delivery completes.
    /// </summary>
    public SubscriptionAddOn AddAddOn(Guid productId, int quantity, string? note = null)
    {
        EnsureEditable();

        var existing = _addOns.FirstOrDefault(a => a.IsPending && a.ProductId == productId);
        if (existing is not null)
        {
            existing.ChangeQuantity(existing.Quantity + quantity);
            Touch();
            return existing;
        }

        var addOn = SubscriptionAddOn.Create(Id, productId, quantity, note);
        _addOns.Add(addOn);
        Touch();

        return addOn;
    }

    public void ChangeAddOnQuantity(Guid addOnId, int quantity)
    {
        EnsureEditable();

        var addOn = _addOns.FirstOrDefault(a => a.Id == addOnId)
            ?? throw new DomainException($"Add-on '{addOnId}' was not found on this subscription.");

        addOn.ChangeQuantity(quantity);
        Touch();
    }

    /// <summary>
    /// Cancels a pending add-on before it ships. Consumed add-ons are history and cannot be removed.
    /// </summary>
    public void RemoveAddOn(Guid addOnId)
    {
        EnsureEditable();

        var addOn = _addOns.FirstOrDefault(a => a.Id == addOnId);
        if (addOn is null) return;

        if (!addOn.IsPending)
            throw new DomainException("A consumed add-on cannot be removed; it is part of delivery history.");

        _addOns.Remove(addOn);
        Touch();
    }

    // --- Delivery resolution -----------------------------------------------

    /// <summary>
    /// Resolves what ships on the next delivery: every static item, the current pick from each
    /// active rotation group, and every pending add-on.
    /// </summary>
    /// <remarks>
    /// Lines stay itemised, so a product arriving from two sources appears twice with its source
    /// recorded. Call <see cref="DeliveryManifest.Consolidate"/> when only totals matter.
    /// </remarks>
    public DeliveryManifest BuildNextDeliveryManifest() => BuildManifestFor(NextDeliveryDate);

    /// <summary>
    /// Completes the next delivery: advances every active rotation, consumes pending add-ons,
    /// and rolls the delivery dates forward. Returns what shipped, for the delivery log.
    /// </summary>
    public DeliveryManifest CompleteDelivery(DateTime deliveredOn)
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription cannot complete a delivery.");

        DateTime deliveryDate = deliveredOn.Date;
        DeliveryManifest manifest = BuildManifestFor(deliveryDate);

        foreach (var group in _rotationGroups.Where(g => g.IsActive))
        {
            group.Advance();
        }

        foreach (var addOn in _addOns.Where(a => a.IsPending).ToList())
        {
            addOn.MarkConsumed(deliveryDate);
        }

        LastDeliveryDate = deliveryDate;
        NextDeliveryDate = Frequency.CalculateNextDate(deliveryDate).Date;

        if (Status == SubscriptionStatus.NewSignUp)
        {
            Status = SubscriptionStatus.Active;
        }

        Touch();

        return manifest;
    }

    /// <summary>
    /// Pushes the next delivery out by one cycle. Nothing shipped, so the rotation does not
    /// advance and pending add-ons survive to the following delivery.
    /// </summary>
    public void SkipNextDelivery()
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription has no deliveries to skip.");

        NextDeliveryDate = Frequency.CalculateNextDate(NextDeliveryDate).Date;
        Touch();
    }

    /// <summary>
    /// Moves the next delivery to a specific date without changing the cadence.
    /// </summary>
    public void Reschedule(DateTime newDeliveryDate)
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription cannot be rescheduled.");

        if (newDeliveryDate.Date < DateTime.UtcNow.Date)
            throw new DomainException("Delivery date cannot be in the past.");

        NextDeliveryDate = newDeliveryDate.Date;
        Touch();
    }

    /// <summary>
    /// Modifies the shipping cadence, optionally shifting the next delivery date to match.
    /// </summary>
    public void ChangeFrequency(OrderFrequency newFrequency, bool recalculateNextDelivery = true)
    {
        ArgumentNullException.ThrowIfNull(newFrequency);

        Frequency = newFrequency;

        if (recalculateNextDelivery)
        {
            var baseDate = LastDeliveryDate ?? DateTime.UtcNow;
            NextDeliveryDate = Frequency.CalculateNextDate(baseDate).Date;
        }

        Touch();
    }

    public void ChangeFulfillmentMethod(FulfillmentMethod method)
    {
        FulfillmentMethod = method;
        Touch();
    }

    // --- Lifecycle ---------------------------------------------------------

    /// <summary>
    /// Moves a new sign-up into active service. Requires something to actually ship.
    /// </summary>
    public void Activate()
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription cannot be reactivated.");

        if (HasNothingScheduled)
            throw new DomainException("A subscription needs at least one item or active rotation group before it can be activated.");

        Status = SubscriptionStatus.Active;
        Touch();
    }

    public void Pause()
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription cannot be paused.");

        Status = SubscriptionStatus.Paused;
        Touch();
    }

    public void Resume()
    {
        if (Status != SubscriptionStatus.Paused)
            throw new DomainException("Only a paused subscription can be resumed.");

        Status = SubscriptionStatus.Active;

        // A subscription paused across its delivery date would otherwise resume in the past.
        if (NextDeliveryDate < DateTime.UtcNow.Date)
        {
            NextDeliveryDate = Frequency.CalculateNextDate(DateTime.UtcNow.Date).Date;
        }

        Touch();
    }

    public void Cancel()
    {
        Status = SubscriptionStatus.Canceled;
        Touch();
    }

    // --- Internals ---------------------------------------------------------

    private DeliveryManifest BuildManifestFor(DateTime deliveryDate)
    {
        var lines = new List<DeliveryLineItem>();

        foreach (var item in _items.OrderBy(i => i.CreatedAt))
        {
            lines.Add(new DeliveryLineItem(item.ProductId, item.Quantity, DeliveryLineSource.Recurring, item.Id));
        }

        foreach (var group in _rotationGroups.Where(g => g.IsActive).OrderBy(g => g.CreatedAt))
        {
            var current = group.CurrentItem;
            if (current is null) continue;

            lines.Add(new DeliveryLineItem(current.ProductId, current.Quantity, DeliveryLineSource.Rotation, group.Id));
        }

        foreach (var addOn in _addOns.Where(a => a.IsPending).OrderBy(a => a.CreatedAt))
        {
            lines.Add(new DeliveryLineItem(addOn.ProductId, addOn.Quantity, DeliveryLineSource.AddOn, addOn.Id));
        }

        return new DeliveryManifest(deliveryDate, lines);
    }

    private void EnsureEditable()
    {
        if (Status == SubscriptionStatus.Canceled)
            throw new DomainException("A canceled subscription cannot be modified.");
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
