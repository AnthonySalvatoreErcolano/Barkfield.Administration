using Barkfield.Administration.API;
using Barkfield.Administration.API.Middleware;
using Barkfield.Administration.API.Bootstrap;
using Scalar.AspNetCore;

public partial class Program
{
    private static void Main(string[] args)
    {
        // Bootstrap commands run instead of the web host, so the first administrator can be
        // created before anyone can sign in to create one.
        if (CreateAdminCommand.Matches(args))
        {
            Environment.ExitCode = CreateAdminCommand.RunAsync(args).GetAwaiter().GetResult();
            return;
        }

        var builder = WebApplication.CreateBuilder(args);
        builder.Resolver();

        var app = builder.Build();

        // First in the pipeline so it catches everything downstream.
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            // MapOpenApi serves the spec as JSON; Scalar renders it as a browsable,
            // executable reference at /scalar. Development only — it documents every
            // endpoint and request shape, which is not something to publish.
            app.MapOpenApi();
            app.MapScalarApiReference(options =>
            {
                options.Title = "Barkfield Road — Admin API";
                options.Theme = ScalarTheme.BluePlanet;
            });
        }
        else
        {
            // Only redirect where TLS is actually terminated by this app. In development the
            // API is served over plain HTTP, and redirecting there breaks the cookie flow.
            app.UseHttpsRedirection();
        }

        app.UseRouting();

        // CORS must sit between routing and auth so preflight requests are answered before
        // anything tries to authenticate them.
        app.UseCors(DependencyResolver.CorsPolicy);

        app.UseRateLimiter();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();

        app.Run();
    }
}
