using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Deliveries;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Deliveries;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Deliveries.Models;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Deliveries: what is going out, whether the stock is in, and what happened.
/// </summary>
/// <remarks>
/// <para>
/// A delivery is a snapshot. Once generated it stops tracking its subscription — product names
/// and prices are frozen at that moment, and editing the subscription afterwards does not change
/// what is in the box. Change the delivery instead.
/// </para>
/// <para>
/// Contents can be edited until the customer has been charged. After that, adding, removing,
/// repricing or substituting a line would mean they paid for a different box from the one they
/// receive, so those are refused. Recording what actually happened — received, out of stock,
/// shorted — stays open, because a paid delivery that came up short is a refund to arrange, not
/// something to leave out of the record.
/// </para>
/// <para>
/// <c>delivery:view</c> to read, <c>delivery:pack</c> for the procurement toggles,
/// <c>delivery:manage</c> for generation, contents and lifecycle.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/deliveries")]
public class DeliveriesController(DeliveryService deliveryService) : ControllerBase
{
    private readonly DeliveryService _deliveryService = deliveryService;

    // --- Reads -------------------------------------------------------------

    /// <summary>
    /// Returns deliveries — the worklist and the dispatch screen.
    /// </summary>
    /// <param name="procurementStatus">
    /// 1 NotStarted, 2 InProgress, 3 Blocked, 4 Ready. <c>Blocked</c> is "what needs a decision";
    /// <c>Ready</c> is "what can be packed".
    /// </param>
    /// <param name="status">1 Scheduled, 2 Packed, 3 Routed, 4 OutForDelivery, 5 Delivered, 6 Failed, 7 Canceled.</param>
    /// <param name="includeClosed">Delivered, failed and cancelled are excluded by default.</param>
    /// <param name="sortBy">One of: scheduledFor, customer, status, procurement, createdAt, total.</param>
    [HttpGet]
    [RequirePermission(Permissions.Deliveries.View)]
    [ProducesResponseType(typeof(PagedResult<DeliveryListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeliveries(
        [FromQuery] string? searchTerm,
        [FromQuery] Guid? customerId,
        [FromQuery] Guid? subscriptionId,
        [FromQuery] DateTime? scheduledFrom,
        [FromQuery] DateTime? scheduledTo,
        [FromQuery] DeliveryStatus? status,
        [FromQuery] ProcurementStatus? procurementStatus,
        [FromQuery] FulfillmentMethod? fulfillmentMethod,
        [FromQuery] bool? hasPaid,
        [FromQuery] bool? oneOffOnly,
        [FromQuery] bool includeClosed = false,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var filter = new DeliveryFilter(
            searchTerm,
            customerId,
            subscriptionId,
            scheduledFrom,
            scheduledTo,
            status,
            procurementStatus,
            fulfillmentMethod,
            hasPaid,
            oneOffOnly,
            includeClosed,
            pageNumber,
            pageSize,
            sortBy,
            sortDescending);

        PagedResult<DeliveryListItemDto> result = await _deliveryService.SearchAsync(filter, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns one delivery with its lines.
    /// </summary>
    /// <remarks>
    /// The address is the snapshot taken when the delivery was scheduled. The phone number, access
    /// notes and stop duration are read live from the customer — a gate code changed this morning
    /// applies to tonight's run.
    /// </remarks>
    [HttpGet("{deliveryId:guid}", Name = nameof(GetDeliveryById))]
    [RequirePermission(Permissions.Deliveries.View)]
    [ProducesResponseType(typeof(DeliveryDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDeliveryById(
        [FromRoute] Guid deliveryId,
        CancellationToken cancellationToken)
    {
        DeliveryDetailDto delivery = await _deliveryService.GetAsync(deliveryId, cancellationToken);

        return Ok(delivery);
    }

    /// <summary>
    /// Returns a customer's delivery history, most recent first.
    /// </summary>
    [HttpGet("/api/customers/{customerId:guid}/deliveries")]
    [RequirePermission(Permissions.Deliveries.View)]
    [ProducesResponseType(typeof(PagedResult<DeliveryListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeliveriesForCustomer(
        [FromRoute] Guid customerId,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken cancellationToken = default)
    {
        var filter = new DeliveryFilter(
            CustomerId: customerId,
            IncludeClosed: true,
            PageNumber: pageNumber,
            PageSize: pageSize,
            SortBy: "scheduledFor",
            SortDescending: true);

        PagedResult<DeliveryListItemDto> result = await _deliveryService.SearchAsync(filter, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// The printable delivery sheet for a date range.
    /// </summary>
    /// <remarks>
    /// One entry per delivery: who it is for, what to pull, and the day. A range rather than one
    /// date because the prep day covers more than one delivery day. Cancelled deliveries are left
    /// out; shorted lines are kept so nothing looks lost off the sheet.
    /// </remarks>
    /// <param name="from">First delivery date to include.</param>
    /// <param name="to">Last delivery date to include. Defaults to <paramref name="from"/>.</param>
    [HttpGet("sheet")]
    [RequirePermission(Permissions.Deliveries.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<DeliverySheetDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDeliverySheet(
        [FromQuery] DateTime from,
        [FromQuery] DateTime? to,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<DeliverySheetDto> sheet =
            await _deliveryService.GetSheetAsync(from, to ?? from, cancellationToken);

        return Ok(sheet);
    }

    /// <summary>
    /// What generation would create for a date, without creating it.
    /// </summary>
    /// <remarks>
    /// Every reason a subscription would be passed over is stated on the row, so nothing is
    /// discovered after the fact. Includes anything <b>overdue</b>, not just due on the day.
    /// </remarks>
    [HttpGet("due")]
    [RequirePermission(Permissions.Deliveries.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<DueSubscriptionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetDue(
        [FromQuery] DateTime date,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<DueSubscriptionDto> due = await _deliveryService.GetDueAsync(date, cancellationToken);

        return Ok(due);
    }

    // --- Create ------------------------------------------------------------

    /// <summary>
    /// Creates deliveries for every subscription due on a date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One date per run. Idempotent — a subscription that already has a delivery for the date is
    /// skipped, and the database enforces that too, so two staff pressing this at once cannot
    /// produce duplicates.
    /// </para>
    /// <para>
    /// Subscriptions whose dated pause has come round are resumed here, which is the moment that
    /// date takes effect.
    /// </para>
    /// <para>
    /// Partial success is normal: a customer with no address is reported and skipped rather than
    /// stopping the rest of the day.
    /// </para>
    /// </remarks>
    /// <response code="400">The date is in the past.</response>
    /// <response code="409">A subscription changed mid-run; nothing was created.</response>
    [HttpPost("generate")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(typeof(DeliveryGenerationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Generate(
        [FromBody] GenerateDeliveriesRequest request,
        CancellationToken cancellationToken)
    {
        DeliveryGenerationResult result =
            await _deliveryService.GenerateAsync(request.DeliveryDate, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Creates a one-off delivery for a customer already in the system.
    /// </summary>
    /// <remarks>
    /// For someone who wants something on a day they are not scheduled, without changing their
    /// subscription. It has contents, gets procured and gets billed like any other delivery — it
    /// just has no recurring order behind it.
    /// </remarks>
    [HttpPost]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateOneOff(
        [FromBody] CreateOneOffDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var lines = request.Lines.Select(l => (l.ProductId, l.Quantity)).ToList();

        Guid deliveryId = await _deliveryService.CreateOneOffAsync(
            request.CustomerId,
            request.ScheduledFor,
            request.FulfillmentMethod,
            lines,
            request.Notes,
            cancellationToken);

        return CreatedAtRoute(nameof(GetDeliveryById), new { deliveryId }, deliveryId);
    }

    // --- Contents ----------------------------------------------------------

    /// <summary>
    /// Adds a product to this delivery by hand.
    /// </summary>
    /// <remarks>
    /// The "she called and wants a bag added" case. Adding a product already on the delivery
    /// raises that line instead of creating a second one for the same thing.
    /// </remarks>
    /// <response code="400">The delivery has been charged, or the product is discontinued.</response>
    [HttpPost("{deliveryId:guid}/lines")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddLine(
        [FromRoute] Guid deliveryId,
        [FromBody] AddDeliveryLineRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.AddLineAsync(deliveryId, request.ProductId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Changes how much of a product is going out.
    /// </summary>
    /// <remarks>
    /// Allowed on a line that came from a subscription: the delivery then diverges from it, which
    /// is intended. Received units reset, because the count no longer describes the new quantity.
    /// </remarks>
    [HttpPut("{deliveryId:guid}/lines/{lineId:guid}/quantity")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeLineQuantity(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ChangeDeliveryLineQuantityRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.ChangeLineQuantityAsync(deliveryId, lineId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Takes a product off this delivery entirely.
    /// </summary>
    /// <remarks>
    /// Different from shorting: shorting says "we meant to send this and could not" and stays on
    /// the record. Removing says it was never meant to go.
    /// </remarks>
    [HttpDelete("{deliveryId:guid}/lines/{lineId:guid}")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveLine(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        CancellationToken cancellationToken)
    {
        await _deliveryService.RemoveLineAsync(deliveryId, lineId, cancellationToken);

        return NoContent();
    }

    // --- Procurement -------------------------------------------------------

    /// <summary>Marks a line as on order with the supplier.</summary>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/ordered")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkLineOrdered(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ProcurementNoteRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkLineOrderedAsync(deliveryId, lineId, request?.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Records stock physically received and set aside.
    /// </summary>
    /// <remarks>
    /// Omit the quantity for the whole line. A number below the line quantity leaves it partially
    /// received, so it stays on the worklist. There is deliberately no bulk receive — "received"
    /// has to mean somebody handled the stock.
    /// </remarks>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/received")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReceiveLine(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ReceiveDeliveryLineRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.ReceiveLineAsync(
            deliveryId, lineId, request?.QuantityReceived, request?.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>Flags a line as unfillable. Blocks packing until substituted or shorted.</summary>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/out-of-stock")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkLineOutOfStock(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ProcurementNoteRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkLineOutOfStockAsync(deliveryId, lineId, request?.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Swaps a line for a different product, keeping the original on the record so the delivery
    /// still shows what was meant to ship.
    /// </summary>
    /// <response code="400">The delivery has been charged, or the substitute is the same product.</response>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/substitute")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubstituteLine(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] SubstituteDeliveryLineRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.SubstituteLineAsync(
            deliveryId, lineId, request.SubstituteProductId, request.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Ships without the line, knowingly. Resolves an out-of-stock.
    /// </summary>
    /// <remarks>
    /// Still allowed after the delivery has been charged: a paid delivery that came up short is a
    /// refund to arrange, and refusing to record it would only make the data lie.
    /// </remarks>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/short")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ShortLine(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ProcurementNoteRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.ShortLineAsync(deliveryId, lineId, request?.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>Returns a line to untouched, for correcting a mis-click.</summary>
    [HttpPost("{deliveryId:guid}/lines/{lineId:guid}/reset")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResetLine(
        [FromRoute] Guid deliveryId,
        [FromRoute] Guid lineId,
        [FromBody] ProcurementNoteRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.ResetLineAsync(deliveryId, lineId, request?.Note, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Puts every unresolved line on order in one action — placing a PO for a delivery is a single
    /// act.
    /// </summary>
    [HttpPost("{deliveryId:guid}/order-all")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkAllOrdered(
        [FromRoute] Guid deliveryId,
        [FromBody] ProcurementNoteRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkAllOrderedAsync(deliveryId, request?.Note, cancellationToken);

        return NoContent();
    }

    // --- Workflow ----------------------------------------------------------

    /// <summary>
    /// Marks the delivery packed.
    /// </summary>
    /// <response code="400">A line is still unresolved. The message names which.</response>
    [HttpPost("{deliveryId:guid}/pack")]
    [RequirePermission(Permissions.Deliveries.Pack)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkPacked(
        [FromRoute] Guid deliveryId,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkPackedAsync(deliveryId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Records a completed delivery and advances the subscription behind it.
    /// </summary>
    /// <remarks>
    /// Both happen in one transaction: the rotation advances, pending add-ons are consumed and the
    /// next delivery date rolls forward. Either all of it lands or none of it does, because half
    /// would leave the standing order describing a dispatch that never happened.
    ///
    /// A one-off has no subscription, so only the delivery is recorded.
    /// </remarks>
    [HttpPost("{deliveryId:guid}/delivered")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkDelivered(
        [FromRoute] Guid deliveryId,
        [FromBody] MarkDeliveredRequest? request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkDeliveredAsync(
            deliveryId, request?.DeliveredOn ?? DateTime.UtcNow, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Records an attempted delivery that did not complete — nobody home, refused, damaged.
    /// </summary>
    /// <remarks>
    /// The subscription is deliberately not advanced: nothing shipped, so the rotation stays put
    /// and pending add-ons survive to the next attempt.
    /// </remarks>
    [HttpPost("{deliveryId:guid}/failed")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> MarkFailed(
        [FromRoute] Guid deliveryId,
        [FromBody] MarkDeliveryFailedRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.MarkFailedAsync(deliveryId, request.Reason, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Cancels a delivery before it ships, and rolls its subscription's next date forward.
    /// </summary>
    /// <remarks>
    /// Both in one transaction. The roll only happens when the subscription still points at the
    /// cancelled date — if it has already moved on, rolling again would skip a delivery nobody
    /// asked to skip.
    /// </remarks>
    [HttpDelete("{deliveryId:guid}")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid deliveryId,
        CancellationToken cancellationToken)
    {
        await _deliveryService.CancelAsync(deliveryId, cancellationToken);

        return NoContent();
    }

    [HttpPut("{deliveryId:guid}/notes")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateNotes(
        [FromRoute] Guid deliveryId,
        [FromBody] UpdateDeliveryNotesRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.UpdateNotesAsync(deliveryId, request.Notes, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Overrides the customer's preferred delivery window for this delivery only. Both ends
    /// together, or neither to clear it.
    /// </summary>
    [HttpPut("{deliveryId:guid}/window")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetWindow(
        [FromRoute] Guid deliveryId,
        [FromBody] SetDeliveryWindowRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.SetRequestedWindowAsync(deliveryId, request.Start, request.End, cancellationToken);

        return NoContent();
    }

    /// <summary>Overrides the customer's default stop duration for this delivery only.</summary>
    [HttpPut("{deliveryId:guid}/service-duration")]
    [RequirePermission(Permissions.Deliveries.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SetServiceDuration(
        [FromRoute] Guid deliveryId,
        [FromBody] SetServiceDurationRequest request,
        CancellationToken cancellationToken)
    {
        await _deliveryService.SetServiceDurationOverrideAsync(deliveryId, request.Minutes, cancellationToken);

        return NoContent();
    }
}
