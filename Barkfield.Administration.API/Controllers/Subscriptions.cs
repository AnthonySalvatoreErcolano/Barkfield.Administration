using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Subscriptions;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Application.Services.Subscriptions.Models;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Barkfield.Administration.Domain.ValueObjects;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Customers' recurring auto-ship orders: cadence, lifecycle, contents and previews.
/// </summary>
/// <remarks>
/// <para>
/// A customer may hold several subscriptions on different cadences, so nothing here refuses a
/// second one and every read returns them as a collection.
/// </para>
/// <para>
/// Contents are edited through sub-resources rather than by replacing the whole subscription.
/// Each of those calls reloads the subscription server-side, applies one change and saves, so
/// two staff working on the same customer do not overwrite each other. A rotation's sequence is
/// in <c>SubscriptionRotationsController</c>.
/// </para>
/// <para>
/// <c>subscription:view</c> to read, <c>subscription:manage</c> to change anything.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/subscriptions")]
public class SubscriptionsController(SubscriptionService subscriptionService) : ControllerBase
{
    private readonly SubscriptionService _subscriptionService = subscriptionService;

    // --- Reads -------------------------------------------------------------

    /// <summary>
    /// Returns subscriptions — paged, searchable and sortable.
    /// </summary>
    /// <param name="searchTerm">Matched against the subscription name and the customer's name and email.</param>
    /// <param name="status">1 NewSignUp, 2 Active, 3 Paused, 4 Canceled.</param>
    /// <param name="includeCanceled">Canceled subscriptions are excluded by default.</param>
    /// <param name="dueFrom">Next delivery on or after this date.</param>
    /// <param name="dueTo">Next delivery on or before this date.</param>
    /// <param name="pauseExpired">
    /// True returns paused subscriptions whose end date has passed. Nothing resumes them
    /// automatically, so this is the worklist for finding them.
    /// </param>
    /// <param name="sortBy">One of: nextDelivery, customer, status, createdAt, lastDelivery, name.</param>
    [HttpGet]
    [RequirePermission(Permissions.Subscriptions.View)]
    [ProducesResponseType(typeof(PagedResult<SubscriptionListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetSubscriptions(
        [FromQuery] string? searchTerm,
        [FromQuery] Guid? customerId,
        [FromQuery] SubscriptionStatus? status,
        [FromQuery] bool includeCanceled,
        [FromQuery] DateTime? dueFrom,
        [FromQuery] DateTime? dueTo,
        [FromQuery] bool? pauseExpired,
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 25,
        [FromQuery] string? sortBy = null,
        [FromQuery] bool sortDescending = false,
        CancellationToken cancellationToken = default)
    {
        var filter = new SubscriptionFilter(
            searchTerm,
            customerId,
            status,
            includeCanceled,
            dueFrom,
            dueTo,
            pauseExpired,
            pageNumber,
            pageSize,
            sortBy,
            sortDescending);

        PagedResult<SubscriptionListItemDto> result =
            await _subscriptionService.SearchAsync(filter, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns one subscription with its static items, rotations and pending add-ons, each
    /// joined to the catalog.
    /// </summary>
    /// <remarks>
    /// Discontinued products are flagged rather than hidden — a product pulled from Square stays
    /// on the subscriptions that reference it, and staff need to see that before it ships.
    /// </remarks>
    [HttpGet("{subscriptionId:guid}", Name = nameof(GetSubscriptionById))]
    [RequirePermission(Permissions.Subscriptions.View)]
    [ProducesResponseType(typeof(SubscriptionDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscriptionById(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        SubscriptionDetailDto subscription = await _subscriptionService.GetAsync(subscriptionId, cancellationToken);

        return Ok(subscription);
    }

    /// <summary>
    /// Returns a customer's subscriptions. Several is normal.
    /// </summary>
    [HttpGet("/api/customers/{customerId:guid}/subscriptions")]
    [RequirePermission(Permissions.Subscriptions.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<SubscriptionListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSubscriptionsForCustomer(
        [FromRoute] Guid customerId,
        [FromQuery] bool includeCanceled,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<SubscriptionListItemDto> subscriptions =
            await _subscriptionService.GetForCustomerAsync(customerId, includeCanceled, cancellationToken);

        return Ok(subscriptions);
    }

    /// <summary>
    /// Previews what the next delivery would contain.
    /// </summary>
    /// <remarks>
    /// Built by the same method that will build the real delivery, so it cannot disagree with
    /// what actually ships. Nothing is written and no rotation advances.
    /// </remarks>
    [HttpGet("{subscriptionId:guid}/next-delivery")]
    [RequirePermission(Permissions.Subscriptions.View)]
    [ProducesResponseType(typeof(DeliveryPreviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetNextDelivery(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        IReadOnlyCollection<DeliveryPreviewDto> previews =
            await _subscriptionService.PreviewUpcomingAsync(subscriptionId, 1, cancellationToken);

        return Ok(previews.Single());
    }

    /// <summary>
    /// Previews the next several deliveries, walking each rotation forward.
    /// </summary>
    /// <param name="cycles">How many deliveries to project, 1–24.</param>
    /// <remarks>
    /// Add-ons appear only on the first cycle, because they are consumed by the delivery they
    /// ship on. Nothing is written.
    /// </remarks>
    [HttpGet("{subscriptionId:guid}/upcoming")]
    [RequirePermission(Permissions.Subscriptions.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<DeliveryPreviewDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUpcomingDeliveries(
        [FromRoute] Guid subscriptionId,
        [FromQuery] int cycles = 4,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyCollection<DeliveryPreviewDto> previews =
            await _subscriptionService.PreviewUpcomingAsync(subscriptionId, cycles, cancellationToken);

        return Ok(previews);
    }

    // --- Create ------------------------------------------------------------

    /// <summary>
    /// Creates a subscription in NewSignUp. It cannot be activated until it has something to ship.
    /// </summary>
    /// <response code="400">The customer is deactivated, or has no address for a local delivery.</response>
    [HttpPost]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CreateSubscription(
        [FromBody] CreateSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        Guid subscriptionId = await _subscriptionService.CreateAsync(
            request.CustomerId,
            request.FrequencyInterval,
            request.FrequencyUnit,
            request.FirstDeliveryDate,
            request.FulfillmentMethod,
            request.Name,
            cancellationToken);

        return CreatedAtRoute(nameof(GetSubscriptionById), new { subscriptionId }, subscriptionId);
    }

    // --- Cadence -----------------------------------------------------------

    /// <summary>Sets or clears the staff-given name.</summary>
    [HttpPut("{subscriptionId:guid}/name")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameSubscription(
        [FromRoute] Guid subscriptionId,
        [FromBody] RenameSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RenameAsync(subscriptionId, request.Name, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Changes the shipping cadence. Any interval and unit — 8 weeks, 1 month, 12 days.
    /// </summary>
    [HttpPut("{subscriptionId:guid}/frequency")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeFrequency(
        [FromRoute] Guid subscriptionId,
        [FromBody] ChangeFrequencyRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ChangeFrequencyAsync(
            subscriptionId,
            request.FrequencyInterval,
            request.FrequencyUnit,
            request.RecalculateNextDelivery,
            cancellationToken);

        return NoContent();
    }

    [HttpPut("{subscriptionId:guid}/fulfillment")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeFulfillmentMethod(
        [FromRoute] Guid subscriptionId,
        [FromBody] ChangeFulfillmentMethodRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ChangeFulfillmentMethodAsync(
            subscriptionId, request.FulfillmentMethod, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Moves the next delivery to a specific date. The manual override — any day of the week.
    /// </summary>
    [HttpPut("{subscriptionId:guid}/schedule")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Reschedule(
        [FromRoute] Guid subscriptionId,
        [FromBody] RescheduleRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RescheduleAsync(subscriptionId, request.NextDeliveryDate, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Pushes the next delivery out by one cycle.
    /// </summary>
    /// <remarks>
    /// Nothing shipped, so rotations do not advance and pending add-ons survive to the following
    /// delivery. That is the difference between skipping and completing.
    /// </remarks>
    [HttpPost("{subscriptionId:guid}/skip")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SkipNextDelivery(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.SkipNextDeliveryAsync(subscriptionId, cancellationToken);

        return NoContent();
    }

    // --- Lifecycle ---------------------------------------------------------

    /// <summary>
    /// Moves a new sign-up into active service.
    /// </summary>
    /// <response code="400">Nothing is scheduled to ship yet.</response>
    [HttpPost("{subscriptionId:guid}/activate")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Activate(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ActivateAsync(subscriptionId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Pauses deliveries, with or without an end date.
    /// </summary>
    /// <remarks>
    /// A dated pause is the vacation case. Nothing wakes up to resume it — the date is stored so
    /// the dashboard can list subscriptions that are due back.
    /// </remarks>
    /// <response code="400">The resume date is today or in the past.</response>
    [HttpPost("{subscriptionId:guid}/pause")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Pause(
        [FromRoute] Guid subscriptionId,
        [FromBody] PauseSubscriptionRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.PauseAsync(subscriptionId, request.ResumeOn, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Resumes a paused subscription. A next delivery date left in the past is moved forward.
    /// </summary>
    [HttpPost("{subscriptionId:guid}/resume")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Resume(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ResumeAsync(subscriptionId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Ends the subscription permanently.
    /// </summary>
    /// <remarks>
    /// Irreversible by design. A customer who comes back gets a new subscription, so this one
    /// stays a closed record of what it actually was. Use pause for anything temporary.
    /// </remarks>
    [HttpDelete("{subscriptionId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(
        [FromRoute] Guid subscriptionId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.CancelAsync(subscriptionId, cancellationToken);

        return NoContent();
    }

    // --- Static items ------------------------------------------------------

    /// <summary>
    /// Adds a product that ships every cycle.
    /// </summary>
    /// <remarks>
    /// Adding a product already on the subscription raises its quantity rather than creating a
    /// duplicate line — there is one line per product, by design.
    /// </remarks>
    /// <response code="400">The product has been discontinued.</response>
    [HttpPost("{subscriptionId:guid}/items")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddItem(
        [FromRoute] Guid subscriptionId,
        [FromBody] AddSubscriptionItemRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.AddItemAsync(
            subscriptionId, request.ProductId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Sets a static line's quantity. Addressed by product, since there is one line per product.
    /// </summary>
    [HttpPut("{subscriptionId:guid}/items/{productId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeItemQuantity(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid productId,
        [FromBody] ChangeQuantityRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ChangeItemQuantityAsync(
            subscriptionId, productId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>Removes a static line.</summary>
    [HttpDelete("{subscriptionId:guid}/items/{productId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveItem(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid productId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RemoveItemAsync(subscriptionId, productId, cancellationToken);

        return NoContent();
    }

    // --- Add-ons -----------------------------------------------------------

    /// <summary>
    /// Attaches a product to the next delivery only.
    /// </summary>
    /// <remarks>
    /// There is deliberately no target date: an add-on applies to whichever delivery happens
    /// next, so rescheduling the subscription cannot orphan it. It is consumed automatically
    /// when that delivery completes.
    /// </remarks>
    [HttpPost("{subscriptionId:guid}/add-ons")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddAddOn(
        [FromRoute] Guid subscriptionId,
        [FromBody] AddAddOnRequest request,
        CancellationToken cancellationToken)
    {
        Guid addOnId = await _subscriptionService.AddAddOnAsync(
            subscriptionId, request.ProductId, request.Quantity, request.Note, cancellationToken);

        return Ok(addOnId);
    }

    [HttpPut("{subscriptionId:guid}/add-ons/{addOnId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeAddOnQuantity(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid addOnId,
        [FromBody] ChangeQuantityRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ChangeAddOnQuantityAsync(
            subscriptionId, addOnId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Cancels a pending add-on before it ships.
    /// </summary>
    /// <response code="400">The add-on has already shipped and is part of delivery history.</response>
    [HttpDelete("{subscriptionId:guid}/add-ons/{addOnId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveAddOn(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid addOnId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RemoveAddOnAsync(subscriptionId, addOnId, cancellationToken);

        return NoContent();
    }
}
