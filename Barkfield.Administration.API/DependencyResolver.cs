using Barkfield.Administration.Application;
using Barkfield.Administration.Infrastructure;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using System.Threading.RateLimiting;

namespace Barkfield.Administration.API;

public static class DependencyResolver
{
    /// <summary>Rate limit policy applied to sign-in and password reset.</summary>
    public const string AuthRateLimitPolicy = "auth";

    /// <summary>CORS policy allowing the admin site to call the API with credentials.</summary>
    public const string CorsPolicy = "AdminPortal";

    public static void Resolver(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddOpenApi();
        builder.Services.AddSignalR();
        builder.Services.AddMemoryCache();

        builder.ConfigureCors();
        builder.ConfigureAuthentication();
        builder.ConfigureRateLimiting();

        builder.Services.Configure<CookieSettings>(
            builder.Configuration.GetSection(CookieSettings.SectionName));

        builder.Services.AddInfrastructure(builder.Configuration);
        builder.Services.AddApplication();
    }

    /// <summary>
    /// Allows the admin site, hosted on a different origin, to call the API.
    /// </summary>
    /// <remarks>
    /// Origins must be listed explicitly. A browser refuses to send cookies to a wildcard
    /// origin, and the refresh token travels as a cookie — so <c>AllowAnyOrigin</c> would
    /// silently break the session refresh it is meant to enable.
    /// </remarks>
    private static void ConfigureCors(this WebApplicationBuilder builder)
    {
        string[] origins = builder.Configuration
            .GetSection("Cors:AllowedOrigins")
            .Get<string[]>() ?? [];

        builder.Services.AddCors(options =>
        {
            options.AddPolicy(CorsPolicy, policy =>
            {
                if (origins.Length == 0)
                {
                    // Nothing configured: allow no cross-origin calls rather than guessing.
                    // Same-origin requests are unaffected.
                    policy.WithOrigins(Array.Empty<string>());
                    return;
                }

                policy.WithOrigins(origins)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .AllowCredentials();
            });
        });
    }

    private static void ConfigureAuthentication(this WebApplicationBuilder builder)
    {
        var jwtSettings = builder.Configuration.GetSection(JwtSettings.SectionName).Get<JwtSettings>()
            ?? throw new InvalidOperationException("JwtSettings configuration section is missing.");

        if (string.IsNullOrWhiteSpace(jwtSettings.Secret))
        {
            throw new InvalidOperationException("JWT Secret key must be configured.");
        }

        var key = Encoding.UTF8.GetBytes(jwtSettings.Secret);

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
        .AddJwtBearer(options =>
        {
            options.RequireHttpsMetadata = !builder.Environment.IsDevelopment();
            options.SaveToken = true;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(key),
                ValidateIssuer = true,
                ValidIssuer = jwtSettings.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtSettings.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });

        builder.Services.AddAuthorization();
    }

    /// <summary>
    /// Throttles the credential-accepting endpoints.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Applied to sign-in and the password reset pair only. <c>refresh-token</c> is
    /// deliberately excluded: a legitimate client calls it routinely, several browser tabs
    /// can call it at once, and it is not a practical brute-force target since refresh
    /// tokens are 64 random bytes and a replayed one already ends every session.
    /// </para>
    /// <para>
    /// <b>The limit is generous on purpose.</b> Partitioning is by client address, and the
    /// whole shop sits behind one office IP — so a tight limit would have staff locking each
    /// other out on a Monday morning rather than stopping an attacker.
    /// </para>
    /// <para>
    /// This is a speed bump, not the real defence. Per-account lockout after repeated
    /// failures is the control that actually stops credential stuffing, and it is not
    /// implemented: it needs failure-count and locked-until columns on Users.
    /// </para>
    /// </remarks>
    private static void ConfigureRateLimiting(this WebApplicationBuilder builder)
    {
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            options.AddPolicy(AuthRateLimitPolicy, context =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 30,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });
    }
}
