using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Products;

/// <summary>
/// Imports a Square item variation into the local catalog.
/// </summary>
/// <remarks>
/// The id is a Square <i>variation</i> id, not an item id — the variation is the sellable
/// thing, and the one a subscription line and a Square order both reference. It comes
/// straight from a row returned by <c>GET /api/products/search</c>.
/// </remarks>
public class ImportProductRequest
{
    [Required, MaxLength(128)]
    public string SquareVariationId { get; set; } = string.Empty;
}
