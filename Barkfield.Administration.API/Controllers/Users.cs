using Barkfield.Administration.API.Models.Requests;
using Barkfield.Administration.Application.DataAccess.Dtos;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Requires valid JWT access token
    public class UsersController(IUserQueries userQueries) : ControllerBase
    {
        private readonly IUserQueries _userQueries = userQueries;

        /// <summary>
        /// Fetches user details by user ID.
        /// </summary>
        /// <param name="userId">The unique identifier of the user.</param>
        /// <param name="cancellationToken">Cancellation token for async operations.</param>
        [HttpGet("{userId:guid}")]
        [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserById(
            [FromRoute] Guid userId,
            CancellationToken cancellationToken)
        {
            UserDto? user = await _userQueries.GetUserByIdAsync(userId, cancellationToken);

            if (user is null)
            {
                return NotFound(new { message = $"User with ID '{userId}' was not found." });
            }

            return Ok(user);
        }

        [HttpPost("create-user")]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request,
           CancellationToken cancellationToken)
        {

            if (user is null)
            {
                return NotFound(new { message = $"User with ID '{userId}' was not found." });
            }

            return Ok(user);
        }
    }
}
