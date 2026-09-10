using Barkfield.Administration.API.Filters;
using Barkfield.Administration.API.Models.Requests;
using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Barkfield.Administration.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers
{
    /// <summary>
    /// API endpoint for managing users. Provides functionality to fetch user details and create new users. Requires valid JWT access token for all operations.
    /// </summary>
    /// <param name="userQueries"></param>
    /// <param name="userService"></param>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UsersController(IUserQueries userQueries, UserService userService) : ControllerBase
    {
        private readonly IUserQueries _userQueries = userQueries;
        private readonly UserService _userService = userService;

        /// <summary>
        /// Fetches user details by user ID. Contains user data and roler information. Requires valid JWT access token.
        /// </summary>
        /// <param name="userId">The unique identifier of the user.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        [HttpGet("{userId:guid}")]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserById([FromRoute] Guid userId, CancellationToken cancellationToken)
        {
            UserDto? user = await _userQueries.GetUserByIdAsync(userId, cancellationToken);

            if (user is null)
            {
                return NotFound(new { message = $"User with ID '{userId}' was not found." });
            }

            return Ok(user);
        }



        /// <summary>
        /// Creates a new user account with assigned roles.
        /// </summary>
        /// <param name="request">The user creation parameters.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        /// <returns>The newly created user's ID.</returns>
        [HttpPost("create-user")]
        [RequirePermission("user:create")]
        [ProducesResponseType(typeof(Guid), StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request,
           CancellationToken cancellationToken)
        {
            Guid userId = await _userService.CreateUserAsync(request.Email, request.Name, request.UserRoles, request.Password, cancellationToken);
            return CreatedAtAction(nameof(GetUserById), new { userId }, userId);
        }


        /// <summary>
        /// Updates an existing user's profile information and assigned roles.
        /// </summary>
        /// <param name="request">The updated user details.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        /// <returns>An HTTP 200 OK response on success.</returns>
        [HttpPut("edit-user")]
        [RequirePermission("user:edit")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> EditUser([FromBody] EditUserRequest request, CancellationToken cancellationToken)
        {
            await _userService.UpdateUserAsync( request.UserId, request.Name, request.Email,request.UserRoles, cancellationToken);

            return Ok();
        }
    }
}
