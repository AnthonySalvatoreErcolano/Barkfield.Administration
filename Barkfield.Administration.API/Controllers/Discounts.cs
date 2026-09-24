using Barkfield.Administration.API.Filters;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Sqaure.Dtos;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// The discounts defined in Square, read live.
/// </summary>
/// <remarks>
/// <para>
/// Nothing is mirrored locally. Discounts are the one piece of pricing staff change often —
/// the autoship rate is adjusted in Square and takes effect at the register the same day —
/// and a stale copy here would mean billing a customer a rate the store no longer offers.
/// </para>
/// <para>
/// Only the id is used downstream. When a delivery is billed, the chosen discount ids go on
/// the Square order and Square computes the total; the percentages returned here are for the
/// staff member choosing from a list, never for arithmetic on our side.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/discounts")]
public class DiscountsController(ProductService productService) : ControllerBase
{
    private readonly ProductService _productService = productService;

    /// <summary>
    /// Returns every discount defined in Square, ordered by name.
    /// </summary>
    /// <response code="502">Square is unreachable or returned an error.</response>
    [HttpGet]
    [RequirePermission(Permissions.Products.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<SquareDiscountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> GetDiscounts(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SquareDiscountDto> discounts =
            await _productService.GetDiscountsAsync(cancellationToken);

        return Ok(discounts);
    }
}
