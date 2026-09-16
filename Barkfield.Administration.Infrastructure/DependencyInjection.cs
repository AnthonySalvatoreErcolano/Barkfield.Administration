using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Application.Services.Sqaure;
using Barkfield.Administration.Infrastructure.Connections.Database;
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


            //services.AddHttpClient<ISquareCatalogService, SquareCatalogServiceGateway>(client =>
            //{
            //    client.BaseAddress = new Uri(baseAddress);
            //    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            //    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", squareAccessToken);
            //    client.DefaultRequestHeaders.Add("Square-Version", "2026-06-23"); // Keeps compliance locked to API timeline rules
            //});


            services.AddSingleton<ISqlConnectionFactory>(new SqlConnectionFactory(connectionString));
            services.AddScoped<ISqlExecutor, SqlExecutor>();
            //services.AddScoped<IBlobStorageService, BlobStorageService>();



            return services;
        }

        private static void ConfigureIdentity(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<JwtSettings>(configuration.GetSection("JwtSettings"));

            services.AddSingleton<PasswordHasher>();
            services.AddScoped<IIdentityService, IdentityService>();

            //var jwtSettings = configuration.GetSection("JwtSettings").Get<JwtSettings>()
            //    ?? throw new InvalidOperationException("JWT settings are missing from application settings.");
            //services.AddSingleton(jwtSettings);
            //services.AddScoped<ITokenService, TokenService>();
            //services.AddScoped<IPasswordHasher, PasswordHasher>();
        }
        private static void ConfigureSquare(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<SquareSettings>(configuration.GetSection(SquareSettings.SectionName));

            services.AddSingleton<ISquareClient>(sp =>
            {
                var options = sp.GetRequiredService<IOptions<SquareSettings>>().Value;
              
                Square.Environment env = options.Environment.Equals("Production", StringComparison.OrdinalIgnoreCase)
                    ? Square.Environment.Production
                    : Square.Environment.Sandbox;

                return new SquareClient.Builder()
                    .Environment(env)
                    .AccessToken(options.AccessToken)
                    .Build();
            });

            services.AddScoped<ISquareService, SquareService>();

            return services;

        }
    }
}
