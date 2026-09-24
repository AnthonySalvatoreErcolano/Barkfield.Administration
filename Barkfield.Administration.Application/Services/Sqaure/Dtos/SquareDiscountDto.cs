namespace Barkfield.Administration.Application.Services.Sqaure.Dtos;

/// <summary>
/// A discount defined in Square, read live rather than mirrored locally.
/// </summary>
/// <remarks>
/// Discounts are not copied into our database. Staff maintain them in Square, and a stale
/// local copy would mean billing a customer a rate the store has already changed. The cost
/// of reading them on demand is one API call on the billing screen.
///
/// Exactly one of <see cref="Percentage"/> and <see cref="Amount"/> is set, depending on
/// <see cref="DiscountType"/>. Square computes the actual reduction at order time — we only
/// pass the discount's id, so these values are for display and nothing else.
/// </remarks>
/// <param name="Id">Square catalog object id, passed back when applying the discount to an order.</param>
/// <param name="DiscountType">Square's type string, e.g. FIXED_PERCENTAGE or FIXED_AMOUNT.</param>
/// <param name="Percentage">Percentage off, e.g. 12.0m for a 12% discount. Null for amount discounts.</param>
/// <param name="Amount">Amount off in dollars. Null for percentage discounts.</param>
public record SquareDiscountDto(
    string Id,
    string Name,
    string? DiscountType,
    decimal? Percentage,
    decimal? Amount);
