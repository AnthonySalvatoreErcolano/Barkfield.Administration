using Barkfield.Administration.API.Models.Requests;
using Barkfield.Administration.API.Models.Responses.Identity;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Barkfield.Administration.Infrastructure.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;
using System.Runtime.CompilerServices;
using System.Security.Claims;

namespace Barkfield.Administration.API.Controllers.Identity
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
                Email = result.email,
                UserId = result.userId
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
        
    }
}
