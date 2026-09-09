using Barkfield.Administration.API.Models.Requests;
using Barkfield.Administration.API.Models.Responses.Identity;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Services.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace Barkfield.Administration.API.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class Auth(IIdentityService identityService, ICookieService cookieService) : ControllerBase
    {
        private readonly IIdentityService _identityService = identityService;
        private readonly ICookieService _cookieService = cookieService;

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            AuthenticationResult? result = await _identityService.LoginAsync(request.Email, request.Password);

            if (result is null) return Unauthorized(new { message = "Invalid email or password." });
            _cookieService.SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAt);
            var response = new LoginResponse
            {
                AccessToken = result.AccessToken,
                Email = result.Email,
                UserId = result.UserId
            };

            return Ok(result);
        }


        [HttpPost("logout")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Logout([FromBody] LogoutRequest request, CancellationToken cancellationToken)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
                return Unauthorized();

            string? refreshToken = Request.Cookies["refreshToken"] ?? request?.RefreshToken;

            await _identityService.LogoutAsync(userId, refreshToken, cancellationToken);
            _cookieService.ClearRefreshTokenCookie();
           

            return Ok(new { message = "Logged out successfully" });
        }


        [HttpPost("refresh-token")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
        {
            // 1. Read refresh token from secure HttpOnly cookie
            string? refreshToken = _cookieService.GetRefreshTokenCookie();

            if (string.IsNullOrWhiteSpace(refreshToken))
            {
                return Unauthorized(new { message = "Refresh token is missing." });
            }

            // 2. Delegate token validation and rotation to IdentityService
            AuthenticationResult? result = await _identityService.RefreshTokenAsync(refreshToken, cancellationToken);

            if (result is null)
            {
                // Token was invalid, expired, or revoked -> clear client cookie
                _cookieService.ClearRefreshTokenCookie();
                return Unauthorized(new { message = "Invalid or expired refresh token." });
            }

            // 3. Set the new rotated refresh token in the HttpOnly cookie
            _cookieService.SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

            // 4. Return the new access token
            return Ok(new LoginResponse
            {
                AccessToken = result.AccessToken,
                Email = result.Email,
                UserId = result.UserId
            });
        }

    }
}
