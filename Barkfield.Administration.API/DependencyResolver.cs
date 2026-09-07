using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using System.Text;
using Barkfield.Administration.Infrastructure;
using Barkfield.Administration.Application;
using Microsoft.IdentityModel.Tokens;
namespace Barkfield.Administration.API
{
    public static class DependencyResolver
    {
        public static void Resolver(this WebApplicationBuilder builder)
        {
            builder.Services.AddControllers();
            builder.Services.AddOpenApi();
            builder.Services.AddSignalR();

            var jwtSection = builder.Configuration.GetSection("JwtSettings");
            var jwtSettings = jwtSection.Get<JwtSettings>()
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

            builder.Services.AddInfrastructure(builder.Configuration);
            builder.Services.AddApplication();
        }

    }
}
