using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests.Users;
using Barkfield.Administration.Application.Common;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Services;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Staff accounts and their role assignments.
/// </summary>
/// <remarks>
/// No response from this controller carries a password hash. Read models for users omit it
/// entirely — only the sign-in path loads one, and it never leaves the Application layer.
/// </remarks>
[ApiController]
[Route("api/users")]
[Authorize]
public class UsersController(IUserQueries userQueries, UserService userService) : ControllerBase
{
    private readonly IUserQueries _userQueries = userQueries;
    private readonly UserService _userService = userService;

    /// <summary>
    /// Returns a paged, searchable, sortable list of staff users.
    /// </summary>
    /// <param name="filter">
    /// Deactivated users are excluded unless <c>includeInactive</c> is set. Sort keys: name,
    /// email, createdAt.
    /// </param>
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(PagedResult<UserListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetUsers([FromQuery] UserFilter filter, CancellationToken cancellationToken)
    {
        PagedResult<UserListItemDto> result = await _userQueries.SearchAsync(filter, cancellationToken);

        return Ok(result);
    }

    /// <summary>
    /// Returns one user with their assigned roles.
    /// </summary>
    [HttpGet("{userId:guid}", Name = nameof(GetUserById))]
    [RequirePermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserById([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        UserDetailDto? user = await _userQueries.GetByIdAsync(userId, cancellationToken);

        if (user is null)
        {
            return NotFound(new { message = $"User with ID '{userId}' was not found." });
        }

        return Ok(user);
    }

    /// <summary>
    /// Returns the signed-in user's own profile. Needs no permission beyond being signed in.
    /// </summary>
    [HttpGet("me")]
    [ProducesResponseType(typeof(UserDetailDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetCurrentUser(CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized();
        }

        UserDetailDto? user = await _userQueries.GetByIdAsync(userId, cancellationToken);

        return user is null ? Unauthorized() : Ok(user);
    }

    /// <summary>
    /// Creates a staff account with its roles.
    /// </summary>
    [HttpPost]
    [RequirePermission(Permissions.Users.Create)]
    [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> CreateUser(
        [FromBody] CreateUserRequest request,
        CancellationToken cancellationToken)
    {
        Guid userId = await _userService.CreateUserAsync(
            name: request.Name,
            email: request.Email,
            password: request.Password,
            roleIds: request.RoleIds,
            isAdmin: request.IsAdmin,
            cancellationToken: cancellationToken);

        return CreatedAtRoute(nameof(GetUserById), new { userId }, userId);
    }

    /// <summary>
    /// Updates a user's profile and replaces their roles.
    /// </summary>
    [HttpPut("{userId:guid}")]
    [RequirePermission(Permissions.Users.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateUser(
        [FromRoute] Guid userId,
        [FromBody] UpdateUserRequest request,
        CancellationToken cancellationToken)
    {
        await _userService.UpdateUserAsync(
            userId: userId,
            name: request.Name,
            email: request.Email,
            roleIds: request.RoleIds,
            isAdmin: request.IsAdmin,
            cancellationToken: cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Changes the signed-in user's own password. The current password must be supplied.
    /// </summary>
    [HttpPost("me/change-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> ChangeOwnPassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetCurrentUserId(out Guid userId))
        {
            return Unauthorized();
        }

        await _userService.ChangePasswordAsync(
            userId, request.CurrentPassword, request.NewPassword, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Deactivates a staff account. This is a soft delete — the user is retained because
    /// delivery and audit history reference them.
    /// </summary>
    [HttpDelete("{userId:guid}")]
    [RequirePermission(Permissions.Users.Delete)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateUser([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        // Locking yourself out is never the intent, and recovering needs another admin.
        if (TryGetCurrentUserId(out Guid currentUserId) && currentUserId == userId)
        {
            return BadRequest(new { message = "You cannot deactivate your own account." });
        }

        await _userService.DeactivateUserAsync(userId, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Reactivates a previously deactivated staff account.
    /// </summary>
    [HttpPost("{userId:guid}/reactivate")]
    [RequirePermission(Permissions.Users.Edit)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ReactivateUser([FromRoute] Guid userId, CancellationToken cancellationToken)
    {
        await _userService.ReactivateUserAsync(userId, cancellationToken);

        return NoContent();
    }

    private bool TryGetCurrentUserId(out Guid userId)
    {
        string? value = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out userId);
    }
}
