using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Application.DataAccess.Deliveries;

/// <summary>
/// One product on a delivery, as it was when the delivery was scheduled, plus where its
/// procurement has got to.
/// </summary>
/// <remarks>
/// Name and price are the snapshot, not the current catalog values. Repricing a product must
/// never rewrite what a past delivery says it shipped and charged.
/// </remarks>
public class DeliveryLineDto
{
    public Guid Id { get; set; }
    public Guid ProductId { get; set; }

    /// <summary>Name captured at scheduling time.</summary>
    public string ProductName { get; set; } = string.Empty;

    /// <summary>Price captured at scheduling time.</summary>
    public decimal UnitPrice { get; set; }

    public int Quantity { get; set; }

    public DeliveryLineSource Source { get; set; }

    /// <summary>Null on a manual line — nothing recurring put it there.</summary>
    public Guid? SourceId { get; set; }

    public LineOrderStatus OrderStatus { get; set; }
    public int QuantityReceived { get; set; }

    public Guid? SubstitutedWithProductId { get; set; }
    public string? SubstitutedWithProductName { get; set; }
    public decimal? SubstitutedWithUnitPrice { get; set; }

    public string? StatusNote { get; set; }
    public DateTime? StatusUpdatedAt { get; set; }

    /// <summary>Enum names, so the UI keeps no copy of the numbers.</summary>
    public string SourceName => Source.ToString();
    public string OrderStatusName => OrderStatus.ToString();

    public int QuantityOutstanding => Math.Max(Quantity - QuantityReceived, 0);

    /// <summary>True once this line needs no further procurement action.</summary>
    public bool IsResolved =>
        OrderStatus is LineOrderStatus.Received or LineOrderStatus.Substituted or LineOrderStatus.Shorted;

    /// <summary>True while this line stops the delivery being packed.</summary>
    public bool IsBlocking => OrderStatus == LineOrderStatus.OutOfStock;

    /// <summary>What actually goes in the box — the substitute once substituted.</summary>
    public string PackingName => SubstitutedWithProductName ?? ProductName;

    /// <summary>Mirrors <c>DeliveryLine.LineTotal</c>: nothing when shorted, the substitute's price when swapped.</summary>
    public decimal LineTotal => OrderStatus switch
    {
        LineOrderStatus.Shorted => 0m,
        LineOrderStatus.Substituted => (SubstitutedWithUnitPrice ?? UnitPrice) * Quantity,
        _ => UnitPrice * Quantity
    };
}

/// <summary>
/// The fields every delivery read shares.
/// </summary>
public abstract class DeliveryHeadDto
{
    public Guid Id { get; set; }

    /// <summary>Null for a one-off that belongs to no recurring order.</summary>
    public Guid? SubscriptionId { get; set; }

    /// <summary>The subscription's staff-given name, when there is one.</summary>
    public string? SubscriptionName { get; set; }

    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;

    public DateTime ScheduledFor { get; set; }
    public DateTime? CompletedAt { get; set; }

    public DeliveryStatus Status { get; set; }
    public FulfillmentMethod FulfillmentMethod { get; set; }
    public ProcurementStatus ProcurementStatus { get; set; }

    public PaymentStatus PaymentStatus { get; set; }
    public int PaymentAttemptCount { get; set; }

    /// <summary>Square's error code, verbatim. PAYMENT_METHOD_ERROR means ring the customer.</summary>
    public string? PaymentFailureCode { get; set; }

    public string? PaymentFailureReason { get; set; }
    public DateTime? PaymentAttemptedAt { get; set; }

    public string? SquareOrderId { get; set; }
    public string? SquarePaymentId { get; set; }
    public string? SquareReceiptUrl { get; set; }

    /// <summary>What Square actually took, which is the figure on the customer's receipt.</summary>
    public decimal? AmountCharged { get; set; }

    public bool AstroCompleted { get; set; }

    public string? ExternalOrderId { get; set; }
    public DateTime? SentToRoutingAt { get; set; }

    public string? Notes { get; set; }
    public string? FailureReason { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Optimistic concurrency token. The service reloads inside the request; this is for display.</summary>
    public int Revision { get; set; }

    public string StatusName => Status.ToString();
    public string FulfillmentMethodName => FulfillmentMethod.ToString();
    public string ProcurementStatusName => ProcurementStatus.ToString();
    public string PaymentStatusName => PaymentStatus.ToString();

    public bool IsOneOff => SubscriptionId is null;

    public bool IsClosed => Status is DeliveryStatus.Delivered or DeliveryStatus.Failed or DeliveryStatus.Canceled;

    /// <summary>Ready to pack: every line received, substituted or knowingly shorted.</summary>
    public bool IsReadyToPack => ProcurementStatus == ProcurementStatus.Ready;

    public bool HasPaid => PaymentStatus == PaymentStatus.Paid;

    /// <summary>Contents are fixed once the customer has been charged for them.</summary>
    public bool ContentsAreLocked => PaymentStatus == PaymentStatus.Paid;

    /// <summary>A charge was attempted and refused. Retryable once the card is fixed in Square.</summary>
    public bool PaymentFailed => PaymentStatus == PaymentStatus.Failed;
}

/// <summary>
/// A delivery row for the worklist and the dispatch screen.
/// </summary>
public class DeliveryListItemDto : DeliveryHeadDto
{
    public int LineCount { get; set; }

    /// <summary>Lines still needing a procurement decision. The number staff work down.</summary>
    public int UnresolvedLineCount { get; set; }

    /// <summary>Lines that are out of stock and blocking. Non-zero means it cannot be packed.</summary>
    public int BlockedLineCount { get; set; }

    public int TotalUnits { get; set; }

    /// <summary>Order value at snapshotted prices, accounting for substitutions and shortages.</summary>
    public decimal Total { get; set; }

    /// <summary>Town only — enough to sanity-check a route without the full address.</summary>
    public string? DeliveryCity { get; set; }
}

/// <summary>
/// A delivery with its lines, the customer's contact details, and the address it goes to.
/// </summary>
/// <remarks>
/// This is also what the write path loads: the service rehydrates the aggregate from it, so
/// "load a delivery" has one definition and the read and write paths cannot see different shapes.
/// </remarks>
public class DeliveryDetailDto : DeliveryHeadDto
{
    public IReadOnlyCollection<DeliveryLineDto> Lines { get; set; } = [];

    /// <summary>Discounts staff chose. Only their ids are sent; Square computes the total.</summary>
    public IReadOnlyCollection<DeliveryDiscountDto> Discounts { get; set; } = [];

    // Address as snapshotted onto the delivery, not as the customer stands today.
    public string DeliveryStreet { get; set; } = string.Empty;
    public string DeliveryCity { get; set; } = string.Empty;
    public string DeliveryState { get; set; } = string.Empty;
    public string DeliveryZipCode { get; set; } = string.Empty;
    public decimal? DeliveryLatitude { get; set; }
    public decimal? DeliveryLongitude { get; set; }

    public TimeOnly? RequestedWindowStart { get; set; }
    public TimeOnly? RequestedWindowEnd { get; set; }
    public int? ServiceDurationMinutesOverride { get; set; }

    /// <summary>Read live from the customer, not snapshotted — a gate code changed today applies tonight.</summary>
    public string? CustomerPhoneNumber { get; set; }
    public string? AccessNotes { get; set; }
    public int ServiceDurationMinutes { get; set; }

    public int TotalUnits => Lines.Sum(l => l.Quantity);
    public decimal Total => Lines.Sum(l => l.LineTotal);

    public IReadOnlyCollection<DeliveryLineDto> UnresolvedLines =>
        Lines.Where(l => !l.IsResolved).ToList();

    /// <summary>
    /// Our estimate against what Square actually took. Square prices from its live catalog and
    /// applies tax, so a difference is information rather than an error — but it is worth seeing.
    /// </summary>
    public decimal? ChargeVariance => AmountCharged is null ? null : AmountCharged - Total;

    /// <summary>A paid delivery with a line that never shipped. Refunds are manual in Square.</summary>
    public bool NeedsRefundAttention =>
        PaymentStatus == PaymentStatus.Paid && Lines.Any(l => l.OrderStatus == LineOrderStatus.Shorted);

    /// <summary>What is stopping this being packed, named, for the message on screen.</summary>
    public IReadOnlyCollection<string> PackingBlockers =>
        Lines.Where(l => !l.IsResolved).Select(l => $"{l.ProductName} ({l.OrderStatusName})").ToList();

    /// <summary>The duration actually sent to routing: this delivery's override, else the customer's.</summary>
    public int EffectiveServiceDurationMinutes =>
        ServiceDurationMinutesOverride ?? ServiceDurationMinutes;
}

/// <summary>
/// One row of the printable delivery sheet.
/// </summary>
/// <remarks>
/// Deliberately thin. The sheet is picked up and carried around a stockroom: who it is for, what
/// they are getting, and what day. The column for writing down where the packed items were put is
/// blank on the page — it is a place for a pen, not data we hold.
/// </remarks>
public class DeliverySheetDto
{
    public Guid DeliveryId { get; set; }
    public DateTime ScheduledFor { get; set; }

    public Guid CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }

    /// <summary>Street and town, for telling two customers with the same surname apart.</summary>
    public string? DeliveryStreet { get; set; }
    public string? DeliveryCity { get; set; }

    public FulfillmentMethod FulfillmentMethod { get; set; }
    public string FulfillmentMethodName => FulfillmentMethod.ToString();

    /// <summary>The subscription's name, or null on a one-off. Tells apart a customer's two orders.</summary>
    public string? SubscriptionName { get; set; }

    public IReadOnlyCollection<DeliverySheetLineDto> Lines { get; set; } = [];

    public int TotalUnits => Lines.Sum(l => l.Quantity);
}

/// <summary>One product on the printable sheet: what to fetch and how many.</summary>
public class DeliverySheetLineDto
{
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>Set when the line was substituted, so the sheet shows what to actually pack.</summary>
    public string? SubstitutedWithProductName { get; set; }

    public string PackingName => SubstitutedWithProductName ?? ProductName;

    public LineOrderStatus OrderStatus { get; set; }
    public string OrderStatusName => OrderStatus.ToString();

    /// <summary>Shorted lines are printed struck through rather than hidden, so nothing looks lost.</summary>
    public bool IsShorted => OrderStatus == LineOrderStatus.Shorted;
}

/// <summary>
/// A discount applied to a delivery.
/// </summary>
/// <remarks>
/// The name and rate are the snapshot taken when it was applied, so a delivery from March still
/// says what it was given after somebody edits or deletes that discount in Square. Display only —
/// Square does the arithmetic, and only <see cref="SquareDiscountId"/> is ever sent.
/// </remarks>
public class DeliveryDiscountDto
{
    public string SquareDiscountId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? DiscountType { get; set; }
    public decimal? Percentage { get; set; }
    public decimal? AmountOff { get; set; }
    public DateTime CreatedAt { get; set; }

    public string Label => Percentage is not null
        ? $"{Name} ({Percentage:0.##}%)"
        : AmountOff is not null ? $"{Name} ({AmountOff:C} off)" : Name;
}
