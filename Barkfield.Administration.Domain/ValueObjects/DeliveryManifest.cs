using Barkfield.Administration.Domain.Shared.Exceptions;

namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// A single resolved line on an upcoming delivery. <paramref name="SourceId"/> points back at
/// the SubscriptionItem, RotationGroup or SubscriptionAddOn that produced it.
/// </summary>
public sealed record DeliveryLineItem(
    Guid ProductId,
    int Quantity,
    DeliveryLineSource Source,
    Guid SourceId);

/// <summary>
/// The same product rolled up across every source, for packing slips and cart totals.
/// </summary>
public sealed record ConsolidatedLine(Guid ProductId, int Quantity);

/// <summary>
/// What a subscription resolves to for one delivery: static items, the current pick from each
/// active rotation group, and any pending add-ons. Lines stay itemised so the reason a product
/// is in the box survives; use <see cref="Consolidate"/> when only totals matter.
/// </summary>
public sealed record DeliveryManifest(DateTime DeliveryDate, IReadOnlyCollection<DeliveryLineItem> Lines)
{
    public bool IsEmpty => Lines.Count == 0;

    public int TotalUnits => Lines.Sum(l => l.Quantity);

    /// <summary>
    /// Rolls duplicate products into a single line. A product that appears as both a static item
    /// and the current rotation pick becomes one line with the combined quantity.
    /// </summary>
    public IReadOnlyCollection<ConsolidatedLine> Consolidate() =>
        Lines.GroupBy(l => l.ProductId)
             .Select(g => new ConsolidatedLine(g.Key, g.Sum(l => l.Quantity)))
             .ToList();

    /// <summary>
    /// Guards against scheduling a delivery with nothing in it.
    /// </summary>
    public void EnsureNotEmpty()
    {
        if (IsEmpty)
            throw new DomainException("A delivery cannot be scheduled with no line items.");
    }
}
