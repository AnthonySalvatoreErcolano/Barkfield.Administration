using Barkfield.Administration.Domain.Shared.Exceptions;
using Barkfield.Administration.Domain.ValueObjects;

namespace Barkfield.Administration.Domain.Entities;

/// <summary>
/// One product on a delivery, with the name and price captured at the moment the delivery
/// was scheduled, plus its procurement state.
/// </summary>
/// <remarks>
/// The snapshot matters: renaming or repricing a catalog product must not rewrite what a past
/// delivery says was shipped and charged.
///
/// Procurement is mutated only through <see cref="Delivery"/>, which recalculates its rollup
/// on every change.
/// </remarks>
public class DeliveryLine
{
    public Guid Id { get; private set; }
    public Guid DeliveryId { get; private set; }
    public Guid ProductId { get; private set; }

    /// <summary>Product name as it was when this delivery was scheduled.</summary>
    public string ProductName { get; private set; } = string.Empty;

    /// <summary>Unit price as it was when this delivery was scheduled.</summary>
    public decimal UnitPrice { get; private set; }

    public int Quantity { get; private set; }

    /// <summary>Why this product is on the delivery.</summary>
    public DeliveryLineSource Source { get; private set; }

    /// <summary>
    /// The SubscriptionItem, RotationGroup or SubscriptionAddOn this line came from.
    /// Null for a <see cref="DeliveryLineSource.Manual"/> line, which came from a person.
    /// </summary>
    public Guid? SourceId { get; private set; }

    // --- Procurement -------------------------------------------------------

    public LineOrderStatus OrderStatus { get; private set; }

    /// <summary>Units physically received and set aside so far.</summary>
    public int QuantityReceived { get; private set; }

    /// <summary>Product that went in the box instead, when this line was substituted.</summary>
    public Guid? SubstitutedWithProductId { get; private set; }

    /// <summary>Substitute product name, snapshotted at substitution time.</summary>
    public string? SubstitutedWithProductName { get; private set; }

    /// <summary>Substitute unit price, snapshotted at substitution time. Drives billing and totals.</summary>
    public decimal? SubstitutedWithUnitPrice { get; private set; }

    /// <summary>Free-text staff note explaining the current status.</summary>
    public string? StatusNote { get; private set; }

    public DateTime? StatusUpdatedAt { get; private set; }

    /// <summary>Units still outstanding.</summary>
    public int QuantityOutstanding => Math.Max(Quantity - QuantityReceived, 0);

    /// <summary>True once this line needs no further procurement action.</summary>
    public bool IsResolved =>
        OrderStatus is LineOrderStatus.Received or LineOrderStatus.Substituted or LineOrderStatus.Shorted;

    /// <summary>True while this line prevents the delivery being packed.</summary>
    public bool IsBlocking => OrderStatus == LineOrderStatus.OutOfStock;

    /// <summary>
    /// What this line actually costs: the substitute's price once substituted, nothing once
    /// shorted, otherwise the original.
    /// </summary>
    public decimal LineTotal => OrderStatus switch
    {
        LineOrderStatus.Shorted => 0m,
        LineOrderStatus.Substituted => (SubstitutedWithUnitPrice ?? UnitPrice) * Quantity,
        _ => UnitPrice * Quantity
    };

    private DeliveryLine() { }

    internal static DeliveryLine Create(
        Guid deliveryId,
        Guid productId,
        string productName,
        decimal unitPrice,
        int quantity,
        DeliveryLineSource source,
        Guid? sourceId)
    {
        if (deliveryId == Guid.Empty)
            throw new DomainException("A delivery line must belong to a valid delivery.");

        if (productId == Guid.Empty)
            throw new DomainException("A delivery line must reference a valid product.");

        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        // Everything except a manual line has to say what put it in the box, so the delivery
        // can still explain itself months later. The database carries the same constraint.
        if (source != DeliveryLineSource.Manual && (sourceId is null || sourceId == Guid.Empty))
            throw new DomainException($"A {source} delivery line must reference the record it came from.");

        return new DeliveryLine
        {
            Id = Guid.NewGuid(),
            DeliveryId = deliveryId,
            ProductId = productId,
            ProductName = productName,
            UnitPrice = unitPrice,
            Quantity = quantity,
            Source = source,
            SourceId = sourceId,
            OrderStatus = LineOrderStatus.Pending,
            QuantityReceived = 0
        };
    }

    public static DeliveryLine FromDto(
        Guid id,
        Guid deliveryId,
        Guid productId,
        string productName,
        decimal unitPrice,
        int quantity,
        DeliveryLineSource source,
        Guid? sourceId,
        LineOrderStatus orderStatus,
        int quantityReceived,
        Guid? substitutedWithProductId,
        string? substitutedWithProductName,
        decimal? substitutedWithUnitPrice,
        string? statusNote,
        DateTime? statusUpdatedAt)
    {
        if (id == Guid.Empty)
            throw new DomainException("Invalid delivery line ID.");

        return new DeliveryLine
        {
            Id = id,
            DeliveryId = deliveryId,
            ProductId = productId,
            ProductName = productName,
            UnitPrice = unitPrice,
            Quantity = quantity,
            Source = source,
            SourceId = sourceId,
            OrderStatus = orderStatus,
            QuantityReceived = quantityReceived,
            SubstitutedWithProductId = substitutedWithProductId,
            SubstitutedWithProductName = substitutedWithProductName,
            SubstitutedWithUnitPrice = substitutedWithUnitPrice,
            StatusNote = statusNote,
            StatusUpdatedAt = statusUpdatedAt
        };
    }

    /// <summary>
    /// Changes how much of this product is going out.
    /// </summary>
    /// <remarks>
    /// Received units are reset, because the count no longer describes the new quantity — a line
    /// raised from 1 to 3 is not "received" just because the first one is on the shelf.
    /// </remarks>
    internal void ChangeQuantity(int quantity)
    {
        if (quantity <= 0)
            throw new DomainException("Quantity must be greater than zero.");

        Quantity = quantity;
        QuantityReceived = 0;
        OrderStatus = LineOrderStatus.Pending;
        ClearSubstitution();
        Touch(null);
    }

    internal void MarkOrdered(string? note)
    {
        OrderStatus = LineOrderStatus.Ordered;
        ClearSubstitution();
        Touch(note);
    }

    /// <summary>
    /// Records units physically received and set aside, deriving the status from the count.
    /// </summary>
    internal void Receive(int quantityReceived, string? note)
    {
        if (quantityReceived < 0)
            throw new DomainException("Received quantity cannot be negative.");

        QuantityReceived = quantityReceived;
        ClearSubstitution();

        OrderStatus = quantityReceived switch
        {
            0 => LineOrderStatus.Ordered,
            _ when quantityReceived >= Quantity => LineOrderStatus.Received,
            _ => LineOrderStatus.PartiallyReceived
        };

        Touch(note);
    }

    internal void MarkOutOfStock(string? note)
    {
        OrderStatus = LineOrderStatus.OutOfStock;
        ClearSubstitution();
        Touch(note);
    }

    internal void Substitute(Guid substituteProductId, string substituteProductName, decimal substituteUnitPrice, string? note)
    {
        if (substituteProductId == Guid.Empty)
            throw new DomainException("A substitute product is required.");

        if (substituteProductId == ProductId)
            throw new DomainException("A line cannot be substituted with the same product.");

        OrderStatus = LineOrderStatus.Substituted;
        SubstitutedWithProductId = substituteProductId;
        SubstitutedWithProductName = substituteProductName;
        SubstitutedWithUnitPrice = substituteUnitPrice;
        QuantityReceived = Quantity;
        Touch(note);
    }

    /// <summary>
    /// Knowingly ships without this line, resolving an out-of-stock.
    /// </summary>
    internal void Short(string? note)
    {
        OrderStatus = LineOrderStatus.Shorted;
        ClearSubstitution();
        QuantityReceived = 0;
        Touch(note);
    }

    /// <summary>
    /// Returns the line to untouched, for correcting a mis-click.
    /// </summary>
    internal void ResetProcurement(string? note)
    {
        OrderStatus = LineOrderStatus.Pending;
        QuantityReceived = 0;
        ClearSubstitution();
        Touch(note);
    }

    private void ClearSubstitution()
    {
        SubstitutedWithProductId = null;
        SubstitutedWithProductName = null;
        SubstitutedWithUnitPrice = null;
    }

    private void Touch(string? note)
    {
        StatusNote = string.IsNullOrWhiteSpace(note) ? null : note.Trim();
        StatusUpdatedAt = DateTime.UtcNow;
    }
}
