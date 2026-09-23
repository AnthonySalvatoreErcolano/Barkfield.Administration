using Barkfield.Administration.API.Models.Requests.Identity;
using Barkfield.Administration.API.Models.Responses.Identity;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Identity.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.Security.Claims;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// Sign-in, session refresh and password reset.
/// </summary>
/// <remarks>
/// <para>
/// The short-lived access token is returned in the response body, where the client needs it.
/// The long-lived refresh token is set as an HttpOnly cookie instead, so JavaScript — and
/// therefore any XSS on the admin site — cannot read it.
/// </para>
/// <para>
/// Sign-in and the password reset pair are rate limited. Refresh is not: a legitimate
/// client calls it routinely and it is not a practical brute-force target.
/// </para>
/// </remarks>
[ApiController]
[Route("api/auth")]

public class AuthController(IIdentityService identityService, ICookieService cookieService) : ControllerBase
{
    private readonly IIdentityService _identityService = identityService;
    private readonly ICookieService _cookieService = cookieService;

    /// <summary>
    /// Signs in and starts a session.
    /// </summary>
    /// <remarks>
    /// Returns the same message whether the email is unknown or the password is wrong, so
    /// the response cannot be used to discover which accounts exist.
    /// </remarks>
    [HttpPost("login")]
    [EnableRateLimiting(DependencyResolver.AuthRateLimitPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        AuthenticationResult? result = await _identityService.LoginAsync(request.Email, request.Password);

        if (result is null)
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        _cookieService.SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

        return Ok(new LoginResponse
        {
            AccessToken = result.AccessToken,
            Email = result.Email,
            UserId = result.UserId
        });
    }

    /// <summary>
    /// Exchanges the refresh cookie for a new access token, rotating the refresh token.
    /// </summary>
    /// <remarks>
    /// Anonymous by design: it is called precisely when the access token has expired, so
    /// requiring one would make it useless. The cookie is the credential.
    ///
    /// Presenting a token that has already been rotated away is treated as theft and ends
    /// every session for that user.
    /// </remarks>
    [HttpPost("refresh-token")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RefreshToken(CancellationToken cancellationToken)
    {
        string? refreshToken = _cookieService.GetRefreshTokenCookie();

        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Unauthorized(new { message = "Refresh token is missing." });
        }

        AuthenticationResult? result = await _identityService.RefreshTokenAsync(refreshToken, cancellationToken);

        if (result is null)
        {
            // Expired, revoked or reused. Clear the cookie so the client stops retrying.
            _cookieService.ClearRefreshTokenCookie();

            return Unauthorized(new { message = "Invalid or expired refresh token." });
        }

        _cookieService.SetRefreshTokenCookie(result.RefreshToken, result.RefreshTokenExpiresAt);

        return Ok(new LoginResponse
        {
            AccessToken = result.AccessToken,
            Email = result.Email,
            UserId = result.UserId
        });
    }

    /// <summary>
    /// Ends the current session and clears the refresh cookie.
    /// </summary>
    [HttpPost("logout")]
    [Authorize]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        string? userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        string? refreshToken = _cookieService.GetRefreshTokenCookie();

        await _identityService.LogoutAsync(userId, refreshToken, cancellationToken);
        _cookieService.ClearRefreshTokenCookie();

        return Ok(new { message = "Logged out successfully." });
    }

    /// <summary>
    /// Starts a password reset.
    /// </summary>
    /// <remarks>
    /// Always reports success, whether or not the email is registered — otherwise this
    /// endpoint becomes a way to discover which addresses have accounts.
    /// </remarks>
    [HttpPost("forgot-password")]
    [EnableRateLimiting(DependencyResolver.AuthRateLimitPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(
        [FromBody] ForgotPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _identityService.RequestPasswordResetAsync(request.Email, cancellationToken);

        return Ok(new { message = "If that email address exists in our system, a password reset link has been sent." });
    }

    /// <summary>
    /// Completes a password reset. Every existing session for the user is revoked.
    /// </summary>
    [HttpPost("reset-password")]
    [EnableRateLimiting(DependencyResolver.AuthRateLimitPolicy)]
    [AllowAnonymous]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResetPassword(
        [FromBody] ResetPasswordRequest request,
        CancellationToken cancellationToken)
    {
        await _identityService.CompletePasswordResetAsync(
            request.Email, request.ResetToken, request.NewPassword, cancellationToken);

        _cookieService.ClearRefreshTokenCookie();

        return Ok(new { message = "Password has been reset. You may now sign in with your new password." });
    }
}
