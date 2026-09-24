namespace Barkfield.Administration.Application.Services.Sqaure.Dtos;

/// <summary>
/// One sellable variation resolved from the Square catalog, with its parent item's name.
/// </summary>
/// <remarks>
/// <para>
/// Square splits a product across two objects: the Item carries the name and description,
/// the ItemVariation carries the price and SKU. Neither is usable alone — the variation is
/// what gets sold and billed, but its name often omits the brand ("Wild-Caught Salmon 7 lb"),
/// so the item's name has to travel with it.
/// </para>
/// <para>
/// <see cref="Price"/> is already converted out of Square's integer cents. It is null when
/// <see cref="IsVariablePricing"/> is set — Square holds no price for those at all, because
/// the amount is keyed in at the till.
/// </para>
/// </remarks>
/// <param name="VariationId">Square catalog object id of the variation. The id an order line references.</param>
/// <param name="ItemId">Square catalog object id of the parent item.</param>
/// <param name="ItemName">The parent item's name, e.g. "Open Farm Kibble".</param>
/// <param name="VariationName">The variation's own name, e.g. "Wild-Caught Salmon 7 lb".</param>
/// <param name="Price">Unit price in dollars, or null for variable pricing.</param>
/// <param name="IsVariablePricing">True when Square expects the price to be entered per sale.</param>
public record SquareVariationDto(
    string VariationId,
    string? ItemId,
    string ItemName,
    string VariationName,
    string? Sku,
    decimal? Price,
    bool IsVariablePricing);
