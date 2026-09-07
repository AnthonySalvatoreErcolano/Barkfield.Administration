using Barkfield.Administration.API;
using Barkfield.Administration.API.Middleware;

public partial class Program
{
    private static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.Resolver();

        var app = builder.Build();
        app.UseMiddleware<ExceptionHandlingMiddleware>();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }


        app.UseHttpsRedirection();
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        //app.MapHub<BatchHub>("/hubs/batch");
        app.MapControllers();

        app.Run();
    }
}