using Barkfield.Administration.Application.DataAccess.Users;
using Barkfield.Administration.Domain.Entities;
using Barkfield.Administration.Infrastructure.Connections.Database;

namespace Barkfield.Administration.Infrastructure.DataAccess.Users;

/// <summary>
/// Writes for users and their role assignments.
/// </summary>
/// <remarks>
/// A user and their roles must land together — a user with no roles cannot sign in usefully,
/// and a half-applied role change is a silent privilege bug. Since <c>ISqlExecutor</c> exposes
/// no transaction scope of its own, the transaction is declared in T-SQL so the whole change
/// is one atomic round trip.
///
/// Role ids are passed as a comma-separated string and expanded with STRING_SPLIT, which
/// keeps the multi-row insert inside that single statement.
/// </remarks>
public class UserCommands : IUserCommands
{
    private readonly ISqlExecutor _sqlExecutor;

    public UserCommands(ISqlExecutor sqlExecutor)
    {
        _sqlExecutor = sqlExecutor;
    }

    public async Task CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            INSERT INTO dbo.Users (Id, Name, Email, PasswordHash, IsActive, IsAdmin, CreatedAt)
            VALUES (@Id, @Name, @Email, @PasswordHash, @IsActive, @IsAdmin, @CreatedAt);

            INSERT INTO dbo.UserRoles (UserId, RoleId)
            SELECT @Id, CAST(value AS UNIQUEIDENTIFIER)
            FROM STRING_SPLIT(@RoleIds, ',')
            WHERE LTRIM(RTRIM(value)) <> '';

            COMMIT TRANSACTION;";

        await _sqlExecutor.ExecuteAsync(
            sql,
            new
            {
                user.Id,
                user.Name,
                user.Email,
                user.PasswordHash,
                user.IsActive,
                user.IsAdmin,
                user.CreatedAt,
                RoleIds = JoinRoleIds(user.RoleIds)
            },
            cancellationToken);
    }

    public async Task UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(user);

        // The password hash is deliberately absent — it is changed only through
        // UpdatePasswordAsync, so an edit cannot overwrite or clear it.
        const string sql = @"
            SET XACT_ABORT ON;
            BEGIN TRANSACTION;

            UPDATE dbo.Users
               SET Name      = @Name,
                   Email     = @Email,
                   IsActive  = @IsActive,
                   IsAdmin   = @IsAdmin,
                   UpdatedAt = @UpdatedAt
             WHERE Id = @Id;

            DELETE FROM dbo.UserRoles WHERE UserId = @Id;

            INSERT INTO dbo.UserRoles (UserId, RoleId)
            SELECT @Id, CAST(value AS UNIQUEIDENTIFIER)
            FROM STRING_SPLIT(@RoleIds, ',')
            WHERE LTRIM(RTRIM(value)) <> '';

            COMMIT TRANSACTION;";

        await _sqlExecutor.ExecuteAsync(
            sql,
            new
            {
                user.Id,
                user.Name,
                user.Email,
                user.IsActive,
                user.IsAdmin,
                UpdatedAt = user.UpdatedAt ?? DateTime.UtcNow,
                RoleIds = JoinRoleIds(user.RoleIds)
            },
            cancellationToken);
    }

    public async Task UpdatePasswordAsync(Guid userId, string passwordHash, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Users
               SET PasswordHash = @PasswordHash,
                   UpdatedAt    = SYSUTCDATETIME()
             WHERE Id = @UserId;";

        await _sqlExecutor.ExecuteAsync(
            sql, new { UserId = userId, PasswordHash = passwordHash }, cancellationToken);
    }

    public async Task<bool> SetActiveAsync(Guid userId, bool isActive, CancellationToken cancellationToken = default)
    {
        const string sql = @"
            UPDATE dbo.Users
               SET IsActive  = @IsActive,
                   UpdatedAt = SYSUTCDATETIME()
             WHERE Id = @UserId;";

        int affected = await _sqlExecutor.ExecuteAsync(
            sql, new { UserId = userId, IsActive = isActive }, cancellationToken);

        return affected > 0;
    }

    private static string JoinRoleIds(IEnumerable<Guid> roleIds) =>
        string.Join(',', roleIds.Select(id => id.ToString()));
}
