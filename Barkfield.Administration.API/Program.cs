using Barkfield.Administration.API;
using Barkfield.Administration.API.Middleware;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Resolver();

        var app = builder.Build();

        // First in the pipeline so it catches everything downstream.
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
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
