namespace Barkfield.Administration.Domain.ValueObjects;

/// <summary>
/// Procurement state of a single product on a single delivery: have we got the stock to fill
/// this line yet?
/// </summary>
/// <remarks>
/// <para>
/// This is the third axis of the original OrderStatus enum, alongside <see cref="DeliveryStatus"/>
/// (fulfilment workflow) and <see cref="FulfillmentMethod"/> (how it ships).
/// </para>
/// <para>
/// Every transition is a deliberate manual action by staff. Nothing moves a line to
/// <see cref="Received"/> automatically, so the status always reflects someone having physically
/// handled and set aside the stock.
/// </para>
/// <para>
/// Staff can go straight from <see cref="Pending"/> to <see cref="Received"/> for anything
/// already on the shelf; <see cref="Ordered"/> is only for stock that had to be brought in.
/// </para>
/// </remarks>
public enum LineOrderStatus
{
    /// <summary>Not yet actioned.</summary>
    Pending = 1,

    /// <summary>On order from the supplier, not yet in hand.</summary>
    Ordered = 2,

    /// <summary>Some units received and set aside, but not the full quantity.</summary>
    PartiallyReceived = 3,

    /// <summary>Full quantity in hand and set aside for this delivery.</summary>
    Received = 4,

    /// <summary>Cannot be filled. Blocks packing until resolved by substitution or shorting.</summary>
    OutOfStock = 5,

    /// <summary>Filled with a different product. Resolves an out-of-stock.</summary>
    Substituted = 6,

    /// <summary>Knowingly shipping without it. Resolves an out-of-stock.</summary>
    Shorted = 7
}
