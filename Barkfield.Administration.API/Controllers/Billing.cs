using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Deliveries;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Billing.Models;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Charging deliveries through Square.
/// </summary>
/// <remarks>
/// <para>
/// Our program chooses the discounts; Square prices the order, applies them, adds tax and takes
/// the money. Barkfield Road's catalog lives in Square and is the source of truth for pricing, so
/// the figure Square computes is the one on the customer's receipt.
/// </para>
/// <para>
/// No card data ever enters this system — only Square's card id — which is what keeps the project
/// out of PCI scope. There is deliberately no endpoint for adding or editing a card: staff do that
/// in Square.
/// </para>
/// <para>
/// A refused card is an outcome, not an error. It comes back in the response body and lands on the
/// needs-attention list, so a batch of thirty charges produces one list of customers to ring
/// rather than stopping at the first refusal.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
public class BillingController(BillingService billingService) : ControllerBase
{
    private readonly BillingService _billingService = billingService;

    /// <summary>
    /// Replaces the discounts applied to a delivery.
    /// </summary>
    /// <remarks>
    /// Validated against Square's live list, so a deleted or mistyped id is caught now rather than
    /// when somebody presses charge. Refused once the delivery has been paid for.
    /// </remarks>
    /// <response code="400">An unknown discount id, or the delivery has already been charged.</response>
    [HttpPut("api/deliveries/{deliveryId:guid}/discounts")]
    [RequirePermission(Permissions.Billing.Charge)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status502BadGateway)]
    public async Task<IActionResult> SelectDiscounts(
        [FromRoute] Guid deliveryId,
        [FromBody] SelectDiscountsRequest request,
        CancellationToken cancellationToken)
    {
        await _billingService.SelectDiscountsAsync(
            deliveryId, request.SquareDiscountIds.ToList(), cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Charges one delivery's card on file.
    /// </summary>
    /// <remarks>
    /// For retrying after a card has been fixed in Square, and for one-offs. Returns 200 with the
    /// outcome even when the card is refused — a decline is information, not a server error.
    /// </remarks>
    [HttpPost("api/deliveries/{deliveryId:guid}/charge")]
    [RequirePermission(Permissions.Billing.Charge)]
    [ProducesResponseType(typeof(DeliveryChargeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Charge(
        [FromRoute] Guid deliveryId,
        CancellationToken cancellationToken)
    {
        DeliveryChargeResult result = await _billingService.ChargeAsync(deliveryId, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Charges every ready, unpaid delivery on a date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The delivery-day action. Deliveries that are already paid, not yet ready, or have no card
    /// are skipped and named; one refusal does not stop the rest.
    /// </para>
    /// <para>
    /// Not one transaction, deliberately: each charge moves real money and cannot be rolled back,
    /// so each is recorded as its outcome is known.
    /// </para>
    /// </remarks>
    [HttpPost("api/deliveries/charge-all")]
    [RequirePermission(Permissions.Billing.Charge)]
    [ProducesResponseType(typeof(BatchChargeResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ChargeDue(
        [FromBody] ChargeDueRequest request,
        CancellationToken cancellationToken)
    {
        BatchChargeResult result = await _billingService.ChargeDueAsync(request.DeliveryDate, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The deliveries somebody has to act on: a card was refused, or a paid delivery came up short
    /// and a refund is owed.
    /// </summary>
    /// <remarks>
    /// Refunds are made by hand in Square, so this list is the only thing making sure one gets
    /// noticed. Unpaid deliveries also stay off the route, so this is the list that has to be
    /// cleared before dispatch.
    /// </remarks>
    [HttpGet("api/deliveries/needs-attention")]
    [RequirePermission(Permissions.Billing.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<ChargeAttentionItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetNeedsAttention(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<ChargeAttentionItem> items =
            await _billingService.GetNeedsAttentionAsync(from, to, cancellationToken);

        return Ok(items);
    }
}
