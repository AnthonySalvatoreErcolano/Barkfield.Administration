using Microsoft.AspNetCore.Http;

namespace Barkfield.Administration.Infrastructure.Settings;

/// <summary>
/// How the refresh-token cookie is written.
/// </summary>
/// <remarks>
/// <para>
/// These have to be configurable because the right answer differs by environment, and the
/// wrong answer fails silently.
/// </para>
/// <para>
/// <b>SameSite matters more than it looks.</b> The admin site is served from one origin
/// (Vercel) and the API from another (Azure). That is a cross-site request, so a
/// <c>SameSite=Strict</c> cookie is never sent — the refresh endpoint would find no cookie
/// and every session would die at the access token's expiry, with no error to explain it.
/// Cross-origin deployments need <c>SameSite=None</c>, which browsers only honour together
/// with <c>Secure</c>, which in turn requires HTTPS on both ends.
/// </para>
/// <para>
/// Locally over plain HTTP the reverse applies: <c>Secure</c> cookies are dropped, so
/// development uses <c>Lax</c> and <c>Secure=false</c>.
/// </para>
/// </remarks>
public class CookieSettings
{
    public const string SectionName = "Cookies";

    /// <summary>Lax, Strict or None. None requires <see cref="Secure"/>.</summary>
    public string SameSite { get; set; } = "Lax";

    /// <summary>Must be true in any deployed environment.</summary>
    public bool Secure { get; set; } = true;

    /// <summary>Optional shared parent domain, when API and UI sit under one domain.</summary>
    public string? Domain { get; set; }

    public SameSiteMode SameSiteMode => SameSite?.ToLowerInvariant() switch
    {
        "none" => SameSiteMode.None,
        "strict" => SameSiteMode.Strict,
        _ => SameSiteMode.Lax
    };
}
