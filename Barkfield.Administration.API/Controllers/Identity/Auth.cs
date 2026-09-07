using Barkfield.Administration.API.Models.Responses.Identity;
using Barkfield.Administration.Application.Services.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.Data;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers.Identity
{
    [Route("api/[controller]")]
    [ApiController]
    public class Auth(IIdentityService identityService) : ControllerBase
    {
        private readonly IIdentityService _identityService = identityService;

        [HttpPost("login")]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            AuthenticationResult? result = await _identityService.LoginAsync(request.Email, request.Password);

            if (result is null)
            {
                return Unauthorized(new { message = "Invalid email or password." });
            }

            return Ok(result);
        }
    }
}
