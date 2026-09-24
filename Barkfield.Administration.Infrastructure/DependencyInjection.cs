using Barkfield.Administration.Application.DataAccess.Allergies;
using Barkfield.Administration.Application.DataAccess.Customers;
using Barkfield.Administration.Application.DataAccess.Pets;
using Barkfield.Administration.Application.DataAccess.Products;
using Barkfield.Administration.Application.DataAccess.Subscriptions;
using Barkfield.Administration.Application.DataAccess.Identity.RefreshTokens;
using Barkfield.Administration.Application.DataAccess.Identity.Roles;
using Barkfield.Administration.Application.DataAccess.Identity.Tokens;
using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Application.Services.Email;
using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Barkfield.Administration.Infrastructure.DataAccess.Allergies;
using Barkfield.Administration.Infrastructure.DataAccess.Customers;
using Barkfield.Administration.Infrastructure.DataAccess.Pets;
using Barkfield.Administration.Infrastructure.DataAccess.Products;
using Barkfield.Administration.Infrastructure.DataAccess.Subscriptions;
using Barkfield.Administration.Infrastructure.DataAccess.RefreshTokens;
using Barkfield.Administration.Infrastructure.DataAccess.Identity.Roles;
using Barkfield.Administration.Infrastructure.DataAccess.Identity.Tokens;
using Barkfield.Administration.Infrastructure.DataAccess.Users;
using Barkfield.Administration.Infrastructure.Services.Email;
using Barkfield.Administration.Infrastructure.Services.Identity;
using Barkfield.Administration.Infrastructure.Services.Square;
using Barkfield.Administration.Infrastructure.Settings;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Square;
using System;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;

namespace Barkfield.Administration.Infrastructure
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            var currentAssembly = typeof(DependencyInjection).Assembly;
            var connectionString = configuration.GetConnectionString("ConnectionString");

            services.ConfigureSquare(configuration);
            services.ConfigureIdentity(configuration);

            services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));
            services.AddScoped<ISqlExecutor, SqlExecutor>();
            //services.AddScoped<IBlobStorageService, BlobStorageService>();

            services.AddDataAccess();

            return services;
        }

        private static void ConfigureIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<JwtSettings>(configuration.GetSection(JwtSettings.SectionName));

            // NOTE: IHttpContextAccessor is registered by the API layer (AddHttpContextAccessor),
            // which is where the ASP.NET Core hosting packages are referenced.
            services.AddSingleton<IPasswordHasher, PasswordHasher>();
            services.AddScoped<ITokenGenerator, TokenGenerator>();
            services.AddScoped<ICookieService, CookieService>();
            services.AddScoped<IIdentityService, IdentityService>();

            // Placeholder transport: logs instead of sending. Swap for a real provider
            // before go-live -- see LoggingEmailService.
            services.AddScoped<IEmailService, LoggingEmailService>();
        }

        /// <summary>
        /// Registers the Dapper-backed query/command implementations of the Application layer contracts.
        /// </summary>
        private static void AddDataAccess(this IServiceCollection services)
        {
            services.AddScoped<ICustomerQueries, CustomerQueries>();
            services.AddScoped<ICustomerCommands, CustomerCommands>();

            services.AddScoped<ISubscriptionQueries, SubscriptionQueries>();
            services.AddScoped<ISubscriptionCommands, SubscriptionCommands>();

            services.AddScoped<IProductQueries, ProductQueries>();
            services.AddScoped<IProductCommands, ProductCommands>();

            services.AddScoped<IPetQueries, PetQueries>();
            services.AddScoped<IPetCommands, PetCommands>();
            services.AddScoped<IAllergyQueries, AllergyQueries>();
            services.AddScoped<IAllergyCommands, AllergyCommands>();

            services.AddScoped<IUserQueries, UserQueries>();
            services.AddScoped<IUserCommands, UserCommands>();
            services.AddScoped<IRoleQueries, RoleQueries>();

            services.AddScoped<IRefreshTokenQueries, RefreshTokenQueries>();
            services.AddScoped<IRefreshTokenCommands, RefreshTokenCommands>();
            services.AddScoped<IPasswordResetTokenQueries, PasswordResetTokenQueries>();
            services.AddScoped<IPasswordResetTokenCommands, PasswordResetTokenCommands>();
        }
        private static void ConfigureSquare(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SquareSettings>(configuration.GetSection(SquareSettings.SectionName));

            services.AddSingleton<ISquareClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SquareSettings>>().Value;

                string baseUrl = options.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    ? SquareEnvironment.Production
                    : SquareEnvironment.Sandbox;

                return new SquareClient(options.AccessToken, new ClientOptions { BaseUrl = baseUrl });
            });

            services.AddScoped<ISquareService, SquareService>();
            services.AddScoped<ISquareCatalogService, SquareCatalogService>();
        }
    }
}
