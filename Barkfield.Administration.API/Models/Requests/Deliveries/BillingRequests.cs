using System.ComponentModel.DataAnnotations;

namespace Barkfield.Administration.API.Models.Requests.Deliveries;

/// <summary>
/// Replaces the discounts applied to a delivery.
/// </summary>
/// <remarks>
/// Wholesale, not merged — the screen shows the full set, so sending it back is what the user
/// means, and an empty list clears them. Ids come from <c>GET /api/discounts</c>; several is normal.
/// </remarks>
public class SelectDiscountsRequest
{
    public IEnumerable<string> SquareDiscountIds { get; set; } = [];
}

/// <summary>Charges every ready, unpaid delivery on a date.</summary>
public class ChargeDueRequest
{
    [Required]
    public DateTime DeliveryDate { get; set; }
}
