using Barkfield.Administration.API.Filters;
using Barkfield.Administration.Application.DataAccess.Identity.Roles;
using Barkfield.Administration.Domain.Entities.Identity.Constants;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Barkfield.Administration.API.Controllers;

/// <summary>
/// The roles a staff user can be assigned.
/// </summary>
/// <remarks>
/// Read-only. Roles and their permission grants are seeded by migration, so that a
/// deployment cannot end up with a role the code expects but the database lacks.
/// </remarks>
[ApiController]
[Route("api/roles")]
[Authorize]
public class RolesController(IRoleQueries roleQueries) : ControllerBase
{
    private readonly IRoleQueries _roleQueries = roleQueries;

    /// <summary>
    /// Returns every role, for the assignment picker on the user form.
    /// </summary>
    [HttpGet]
    [RequirePermission(Permissions.Users.View)]
    [ProducesResponseType(typeof(IReadOnlyCollection<RoleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetRoles(CancellationToken cancellationToken)
    {
        IReadOnlyCollection<RoleDto> roles = await _roleQueries.GetAllAsync(cancellationToken);

        return Ok(roles);
    }
}
