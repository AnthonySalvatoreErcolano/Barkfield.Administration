namespace Barkfield.Administration.Application.Services.Subscriptions.Models;

/// <summary>
/// One line of a previewed delivery, resolved to a real product.
/// </summary>
/// <param name="Source">Recurring, Rotation or AddOn — why this product is in the box.</param>
/// <param name="SourceLabel">
/// The rotation's name for a rotation line, the add-on's note for an add-on, null for a static
/// item. What makes "why is this here?" answerable at a glance.
/// </param>
public record DeliveryPreviewLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    string Source,
    Guid SourceId,
    string? SourceLabel,
    bool ProductIsActive)
{
    public decimal LineTotal => UnitPrice * Quantity;
}

/// <summary>
/// What a subscription would ship on a given date.
/// </summary>
/// <remarks>
/// <para>
/// A projection, not a record of anything — nothing is written and no rotation advances. It
/// answers the question staff actually ask on the phone: "what's in Joe's next box?"
/// </para>
/// <para>
/// <see cref="EstimatedTotal"/> is at current catalog prices and before any discount. Square
/// computes what is actually charged at billing time, so this is a guide, not a quote.
/// </para>
/// </remarks>
public record DeliveryPreviewDto(
    DateTime DeliveryDate,
    int CycleNumber,
    IReadOnlyCollection<DeliveryPreviewLineDto> Lines)
{
    public bool IsEmpty => Lines.Count == 0;

    public int TotalUnits => Lines.Sum(l => l.Quantity);

    /// <summary>At current catalog prices, before discounts. Indicative only.</summary>
    public decimal EstimatedTotal => Lines.Sum(l => l.LineTotal);

    /// <summary>Discontinued products that would ship, for a warning on the preview.</summary>
    public IReadOnlyCollection<string> DiscontinuedProductNames =>
        Lines.Where(l => !l.ProductIsActive).Select(l => l.ProductName).Distinct().ToList();
}
