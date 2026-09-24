using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Subscriptions;

/// <summary>
/// One product on a subscription, joined to the catalog so the UI never shows a bare id.
/// </summary>
/// <remarks>
/// <see cref="ProductIsActive"/> is the payoff from mirroring the catalog locally: a product
/// discontinued in Square stays on the subscriptions that reference it, and this is what lets
/// staff see that before it reaches a delivery.
/// </remarks>
public class SubscriptionLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }

    /// <summary>False when the product has been discontinued in the catalog.</summary>
    public bool ProductIsActive { get; set; }

    public int Quantity { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// One product in a rotation's sequence.
/// </summary>
public class RotationGroupItemDto : SubscriptionLineDto
{
    public Guid RotationGroupId { get; set; }

    /// <summary>1-based position in the rotation.</summary>
    public int SequenceOrder { get; set; }
}

/// <summary>
/// A rotation on a subscription, with its sequence and where the cursor currently sits.
/// </summary>
public class RotationGroupDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;

    /// <summary>Monotonic cursor. Resolved against the item count by modulo.</summary>
    public int RotationPosition { get; set; }

    /// <summary>A paused rotation keeps its contents and position but ships nothing.</summary>
    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    public IReadOnlyList<RotationGroupItemDto> Items { get; set; } = [];

    /// <summary>Complete passes through the rotation so far.</summary>
    public int CompletedCycles => Items.Count == 0 ? 0 : RotationPosition / Items.Count;

    /// <summary>
    /// The item that ships on the next delivery, or null when the rotation is empty.
    /// </summary>
    /// <remarks>
    /// Mirrors <see cref="RotationGroup.CurrentItem"/> — the same modulo of the same cursor.
    /// Duplicated here so the list and detail reads do not have to rehydrate the aggregate
    /// just to answer "what's next".
    /// </remarks>
    public RotationGroupItemDto? CurrentItem =>
        Items.Count == 0 ? null : Items[RotationPosition % Items.Count];

    /// <summary>True when any product in the rotation has been discontinued.</summary>
    public bool HasInactiveProduct => Items.Any(i => !i.ProductIsActive);
}

/// <summary>
/// A one-off add-on. Retained after it ships, so the delivery log can still explain the box.
/// </summary>
public class SubscriptionAddOnDto : SubscriptionLineDto
{
    public string? Note { get; set; }

    /// <summary>Null while the add-on is still waiting to ship.</summary>
    public DateTime? ConsumedAt { get; set; }

    public bool IsPending => ConsumedAt is null;
}

/// <summary>
/// The fields every subscription read shares.
/// </summary>
public abstract class SubscriptionHeadDto
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }

    /// <summary>Staff-given name. Null is normal — see <see cref="DisplayName"/>.</summary>
    public string? Name { get; set; }

    public string CustomerName { get; set; } = string.Empty;

    public SubscriptionStatus Status { get; set; }

    public int FrequencyInterval { get; set; }
    public FrequencyUnit FrequencyUnit { get; set; }

    public FulfillmentMethod FulfillmentMethod { get; set; }

    public DateTime NextDeliveryDate { get; set; }
    public DateTime? LastDeliveryDate { get; set; }
    public DateTime SignUpDate { get; set; }

    /// <summary>Set when a pause has an end date. Null means open-ended.</summary>
    public DateTime? PausedUntil { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// Optimistic concurrency token. A write must present the revision it read; the server
    /// increments it. Clients do not need to send it back — the service reloads inside the
    /// request — but it is here so a UI can show whether its view is stale.
    /// </summary>
    public int Revision { get; set; }

    /// <summary>Enum names, so the UI does not keep its own copy of the numbers.</summary>
    public string StatusName => Status.ToString();
    public string FulfillmentMethodName => FulfillmentMethod.ToString();

    /// <summary>Human cadence, e.g. "every 4 weeks".</summary>
    public string FrequencyLabel => Frequency.ToString();

    /// <summary>A pause that has run out. Delivery generation resumes these.</summary>
    public bool IsPauseExpired =>
        Status == SubscriptionStatus.Paused && PausedUntil is not null && PausedUntil.Value.Date <= DateTime.UtcNow.Date;

    protected OrderFrequency Frequency => OrderFrequency.Every(FrequencyInterval, FrequencyUnit);
}

/// <summary>
/// A subscription row for the dashboard and for a customer's list.
/// </summary>
public class SubscriptionListItemDto : SubscriptionHeadDto
{
    public int ItemCount { get; set; }
    public int RotationGroupCount { get; set; }
    public int PendingAddOnCount { get; set; }

    /// <summary>Discontinued products anywhere on the subscription, for a warning badge.</summary>
    public int InactiveProductCount { get; set; }

    /// <summary>
    /// What staff see in a list or picker. Falls back to cadence and contents when unnamed,
    /// because a customer with two unnamed subscriptions would otherwise show two identical rows.
    /// </summary>
    public string DisplayName =>
        Subscription.ComposeDisplayName(Name, Frequency, ItemCount, RotationGroupCount);
}

/// <summary>
/// A subscription with everything on it: static items, rotations and their sequences, and
/// pending add-ons — all joined to the catalog.
/// </summary>
/// <remarks>
/// This is also what the write path loads. The service rehydrates the aggregate from it rather
/// than issuing a second, leaner query, so there is one definition of "load a subscription".
/// Consumed add-ons are excluded: they are delivery history, and the delivery log is where
/// they belong.
/// </remarks>
public class SubscriptionDetailDto : SubscriptionHeadDto
{
    public IReadOnlyCollection<SubscriptionLineDto> Items { get; set; } = [];
    public IReadOnlyCollection<RotationGroupDto> RotationGroups { get; set; } = [];
    public IReadOnlyCollection<SubscriptionAddOnDto> PendingAddOns { get; set; } = [];

    public string DisplayName =>
        Subscription.ComposeDisplayName(Name, Frequency, Items.Count, RotationGroups.Count);

    /// <summary>True when there is nothing recurring to ship, which blocks activation.</summary>
    public bool HasNothingScheduled =>
        Items.Count == 0 && !RotationGroups.Any(g => g.IsActive);

    /// <summary>Every discontinued product across items, rotations and pending add-ons.</summary>
    public IReadOnlyCollection<string> DiscontinuedProductNames =>
        Items.Where(i => !i.ProductIsActive).Select(i => i.ProductName)
            .Concat(RotationGroups.SelectMany(g => g.Items).Where(i => !i.ProductIsActive).Select(i => i.ProductName))
            .Concat(PendingAddOns.Where(a => !a.ProductIsActive).Select(a => a.ProductName))
            .Distinct()
            .ToList();
}
