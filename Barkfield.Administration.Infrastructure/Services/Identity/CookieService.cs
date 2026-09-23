using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Barkfield.Administration.Infrastructure.Services.Identity;

/// <summary>
/// Reads and writes the refresh-token cookie.
/// </summary>
/// <remarks>
/// The refresh token lives in an HttpOnly cookie rather than in the response body so that
/// JavaScript — and therefore any XSS on the admin site — cannot read it. The short-lived
/// access token is returned in the body instead, where the client does need it.
/// </remarks>
public class CookieService : ICookieService
{
    private const string RefreshTokenCookieName = "refreshToken";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly CookieSettings _settings;

    public CookieService(IHttpContextAccessor httpContextAccessor, IOptions<CookieSettings> settings)
    {
        _httpContextAccessor = httpContextAccessor;
        _settings = settings.Value;
    }

    public void SetRefreshTokenCookie(string refreshToken, DateTime expiresAt)
    {
        _httpContextAccessor.HttpContext?.Response.Cookies.Append(
            RefreshTokenCookieName, refreshToken, BuildOptions(expiresAt));
    }

    public string? GetRefreshTokenCookie() =>
        _httpContextAccessor.HttpContext?.Request.Cookies[RefreshTokenCookieName];

    public void ClearRefreshTokenCookie()
    {
        // Deletion only takes effect if the attributes match those the cookie was written
        // with, so this builds the same options rather than hard-coding a second set.
        _httpContextAccessor.HttpContext?.Response.Cookies.Delete(
            RefreshTokenCookieName, BuildOptions(null));
    }

    private CookieOptions BuildOptions(DateTime? expiresAt)
    {
        var options = new CookieOptions
        {
            HttpOnly = true,
            Secure = _settings.Secure,
            SameSite = _settings.SameSiteMode,
            Path = "/",
            Expires = expiresAt
        };

        if (!string.IsNullOrWhiteSpace(_settings.Domain))
        {
            options.Domain = _settings.Domain;
        }

        return options;
    }
}
