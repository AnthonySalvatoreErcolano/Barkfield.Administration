using System.Security.Claims;
using Barkfield.Administration.Infrastructure.Connections.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Caching.Memory;

namespace Barkfield.Administration.API.Filters;

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public class RequirePermissionAttribute : TypeFilterAttribute
{
    public RequirePermissionAttribute(string permission) : base(typeof(PermissionAuthorizationFilter))
    {
        Arguments = [permission];
    }
}

public class PermissionAuthorizationFilter(string permission,ISqlExecutor sqlExecutor,IMemoryCache cache) :
    IAsyncAuthorizationFilter
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        var user = context.HttpContext.User;

        if (user?.Identity is null || !user.Identity.IsAuthenticated)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userIdString = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userIdString) || !Guid.TryParse(userIdString, out var userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        string cacheKey = $"user_perm_{userId}_{permission}";

        if (!cache.TryGetValue(cacheKey, out bool hasPermission))
        {
            const string checkPermissionSql = @"
                SELECT COUNT(1) 
                FROM Users u
                LEFT JOIN UserPermissions up ON u.Id = up.UserId
                WHERE u.Id = @UserId 
                  AND u.IsActive = 1 
                  AND (up.PermissionName = @Permission OR u.IsAdmin = 1);";

            var count = await sqlExecutor.QuerySingleAsync<int>(checkPermissionSql, new
            {
                UserId = userId,
                Permission = permission
            });

            hasPermission = count > 0;

            var cacheOptions = new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(CacheDuration);

            cache.Set(cacheKey, hasPermission, cacheOptions);
        }

        if (!hasPermission)
        {
            context.Result = new ForbidResult();
        }
    }
}