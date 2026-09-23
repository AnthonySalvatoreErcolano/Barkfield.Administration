using Barkfield.Administration.Application.Services.Identity;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.API.Bootstrap;

/// <summary>
/// Creates the first administrator from the command line.
/// </summary>
/// <remarks>
/// <para>
/// This exists because the chicken-and-egg problem is otherwise painful: no account can be
/// created through the API without signing in, and the password column holds a BCrypt hash
/// that T-SQL cannot produce. Without this, bootstrapping means hand-writing SQL with a hash
/// generated somewhere else — easy to get subtly wrong, and easy to paste a weak or shared
/// hash into production.
/// </para>
/// <para>
/// Hashing goes through the same <see cref="IPasswordHasher"/> the sign-in path uses, so the
/// work factor cannot drift from what the application expects.
/// </para>
/// <para>
/// Usage:
/// <code>
/// dotnet run -- create-admin --email you@barkfieldroad.com --name "Your Name" --password "..."
/// </code>
/// The password may be omitted, in which case it is prompted for without echoing.
/// </para>
/// </remarks>
public static class CreateAdminCommand
{
    public const string CommandName = "create-admin";

    /// <summary>Seeded in migration 002. Fixed across environments.</summary>
    private static readonly Guid AdministratorRoleId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    public static bool Matches(string[] args) =>
        args.Length > 0 && string.Equals(args[0], CommandName, StringComparison.OrdinalIgnoreCase);

    public static async Task<int> RunAsync(string[] args)
    {
        string? email = GetOption(args, "--email");
        string? name = GetOption(args, "--name");
        string? password = GetOption(args, "--password");

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
        {
            Console.Error.WriteLine("Usage: dotnet run -- create-admin --email <email> --name \"<name>\" [--password <password>]");
            return 1;
        }

        password ??= ReadPasswordWithoutEcho();

        if (string.IsNullOrWhiteSpace(password) || password.Length < 12)
        {
            Console.Error.WriteLine("A password of at least 12 characters is required.");
            return 1;
        }

        // A minimal container rather than the full web host: bootstrapping should not
        // require the JWT signing key to be configured first.
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        if (string.IsNullOrWhiteSpace(configuration.GetConnectionString("ConnectionString")))
        {
            Console.Error.WriteLine("ConnectionStrings:ConnectionString is not configured.");
            return 1;
        }

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddInfrastructure(configuration);

        using ServiceProvider provider = services.BuildServiceProvider();

        var sql = provider.GetRequiredService<ISqlExecutor>();
        var hasher = provider.GetRequiredService<IPasswordHasher>();

        bool exists = await sql.QuerySingleAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Users WHERE Email = @Email) THEN 1 ELSE 0 END;",
            new { Email = email.Trim().ToLowerInvariant() });

        if (exists)
        {
            Console.Error.WriteLine($"A user with the email '{email}' already exists.");
            return 1;
        }

        bool roleExists = await sql.QuerySingleAsync<bool>(
            "SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.Roles WHERE Id = @Id) THEN 1 ELSE 0 END;",
            new { Id = AdministratorRoleId });

        if (!roleExists)
        {
            Console.Error.WriteLine("The Administrator role is missing. Apply migration 002 before creating a user.");
            return 1;
        }

        // Goes through the domain factory, so the same validation and normalisation apply
        // as when an administrator creates a user through the API.
        User user = User.Create(name, email, hasher.HashPassword(password), [AdministratorRoleId], isAdmin: true);

        await sql.ExecuteAsync(@"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            INSERT INTO dbo.Users (Id, Name, Email, PasswordHash, IsActive, IsAdmin, CreatedAt)
            VALUES (@Id, @Name, @Email, @PasswordHash, 1, 1, @CreatedAt);

            INSERT INTO dbo.UserRoles (UserId, RoleId) VALUES (@Id, @RoleId);

            COMMIT TRANSACTION;",
            new
            {
                user.Id,
                user.Name,
                user.Email,
                user.PasswordHash,
                user.CreatedAt,
                RoleId = AdministratorRoleId
            });

        Console.WriteLine($"Created administrator {user.Email} ({user.Id}).");

        return 0;
    }

    private static string? GetOption(string[] args, string option)
    {
        int index = Array.FindIndex(args, a => string.Equals(a, option, StringComparison.OrdinalIgnoreCase));

        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    /// <summary>
    /// Reads a password without echoing it, so it does not end up on screen or in shell history.
    /// </summary>
    private static string ReadPasswordWithoutEcho()
    {
        Console.Write("Password: ");

        var builder = new System.Text.StringBuilder();

        while (true)
        {
            ConsoleKeyInfo key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.Enter) break;

            if (key.Key == ConsoleKey.Backspace)
            {
                if (builder.Length > 0) builder.Length--;
                continue;
            }

            if (!char.IsControl(key.KeyChar)) builder.Append(key.KeyChar);
        }

        Console.WriteLine();

        return builder.ToString();
    }
}
