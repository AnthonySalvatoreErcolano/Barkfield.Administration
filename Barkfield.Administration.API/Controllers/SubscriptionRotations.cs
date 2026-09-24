using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Subscriptions;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Rotations on a subscription: ordered sets where exactly one product ships per delivery.
/// </summary>
/// <remarks>
/// <para>
/// The rotating-proteins case. A group holds an ordered sequence, one member ships each cycle,
/// and the cursor advances when a delivery completes — never when one is skipped.
/// </para>
/// <para>
/// Editing a rotation never silently changes what the customer is about to receive: removing a
/// member or reordering the sequence keeps the product that was up next up next. That is a
/// domain guarantee, not something these endpoints arrange.
/// </para>
/// <para>
/// Split from <c>SubscriptionsController</c> only for file size; the routes are subordinate to
/// the subscription and the permissions are the same.
/// </para>
/// </remarks>
[ApiController]
[Authorize]
[Route("api/subscriptions/{subscriptionId:guid}/rotation-groups")]
public class SubscriptionRotationsController(SubscriptionService subscriptionService) : ControllerBase
{
    private readonly SubscriptionService _subscriptionService = subscriptionService;

    /// <summary>
    /// Creates a rotation group. Names must be unique within the subscription.
    /// </summary>
    /// <response code="400">The subscription already has a group with that name.</response>
    [HttpPost]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateRotationGroup(
        [FromRoute] Guid subscriptionId,
        [FromBody] RotationGroupNameRequest request,
        CancellationToken cancellationToken)
    {
        Guid groupId = await _subscriptionService.AddRotationGroupAsync(
            subscriptionId, request.Name, cancellationToken);

        return Ok(groupId);
    }

    [HttpPut("{groupId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RenameRotationGroup(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromBody] RotationGroupNameRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RenameRotationGroupAsync(
            subscriptionId, groupId, request.Name, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes a rotation group and everything in it.
    /// </summary>
    /// <remarks>
    /// A hard removal, unlike most deletes here — a rotation group is a grouping the staff made,
    /// not a record of anything. Past deliveries keep their own snapshot of what shipped.
    /// </remarks>
    [HttpDelete("{groupId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveRotationGroup(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RemoveRotationGroupAsync(subscriptionId, groupId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Pauses a rotation. It keeps its contents and its position but contributes nothing to a
    /// delivery, so pausing and resuming does not lose the customer's place in the sequence.
    /// </summary>
    [HttpPost("{groupId:guid}/pause")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> PauseRotationGroup(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.SetRotationGroupActiveAsync(subscriptionId, groupId, false, cancellationToken);

        return NoContent();
    }

    [HttpPost("{groupId:guid}/resume")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ResumeRotationGroup(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.SetRotationGroupActiveAsync(subscriptionId, groupId, true, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Appends a product to the end of the rotation sequence.
    /// </summary>
    /// <response code="400">The product is already in this rotation, or has been discontinued.</response>
    [HttpPost("{groupId:guid}/items")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> AddRotationItem(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromBody] AddRotationItemRequest request,
        CancellationToken cancellationToken)
    {
        Guid itemId = await _subscriptionService.AddRotationItemAsync(
            subscriptionId, groupId, request.ProductId, request.Quantity, cancellationToken);

        return Ok(itemId);
    }

    /// <summary>
    /// Sets how much of this member ships when its turn comes up. Quantity is per member, so a
    /// two-bag chicken and a one-bag lamb can share the same rotation.
    /// </summary>
    [HttpPut("{groupId:guid}/items/{itemId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ChangeRotationItemQuantity(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromRoute] Guid itemId,
        [FromBody] ChangeQuantityRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ChangeRotationItemQuantityAsync(
            subscriptionId, groupId, itemId, request.Quantity, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Removes a product from the rotation.
    /// </summary>
    /// <remarks>
    /// The sequence is renumbered and the cursor re-pointed, so whatever was scheduled to ship
    /// next still ships next — unless it was the removed product itself.
    /// </remarks>
    [HttpDelete("{groupId:guid}/items/{itemId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveRotationItem(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.RemoveRotationItemAsync(subscriptionId, groupId, itemId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Replaces the rotation order.
    /// </summary>
    /// <remarks>
    /// Must list every item in the group exactly once. The product currently up next stays up
    /// next, so dragging the sequence around cannot change what the customer is about to get.
    /// </remarks>
    /// <response code="400">The list does not match the group's items exactly.</response>
    [HttpPut("{groupId:guid}/order")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ReorderRotation(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromBody] ReorderRotationRequest request,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.ReorderRotationAsync(
            subscriptionId, groupId, request.ItemIdsInOrder, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Forces a specific member to be next out — "give them lamb this time".
    /// </summary>
    /// <remarks>
    /// Moves the cursor within the current cycle rather than rewinding it, so the count of
    /// completed cycles stays honest.
    /// </remarks>
    [HttpPost("{groupId:guid}/jump-to/{itemId:guid}")]
    [RequirePermission(Permissions.Subscriptions.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> JumpRotationTo(
        [FromRoute] Guid subscriptionId,
        [FromRoute] Guid groupId,
        [FromRoute] Guid itemId,
        CancellationToken cancellationToken)
    {
        await _subscriptionService.JumpRotationToAsync(subscriptionId, groupId, itemId, cancellationToken);

        return NoContent();
    }
}
