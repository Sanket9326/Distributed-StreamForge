using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StreamForge.Identity.Api.Data;
using StreamForge.Identity.Api.Models;

namespace StreamForge.Identity.Api.Controllers;

/// <summary>Resolves public display names without exposing account contact details.</summary>
[ApiController]
[Route("api/users")]
public sealed class ProfilesController(IdentityDbContext dbContext) : ControllerBase
{
    /// <summary>Returns safe public display names for up to 50 distinct user IDs.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PublicProfile>>> Get(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        var distinct = ids.Distinct().ToArray();
        if (distinct.Length > 50)
        {
            return BadRequest(new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "Too many profile identifiers",
                Detail = "At most 50 user profiles can be requested at once."
            });
        }

        return await dbContext.Users
            .AsNoTracking()
            .Where(user => distinct.Contains(user.Id))
            .OrderBy(user => user.Username)
            .Select(user => new PublicProfile(user.Id, user.Username))
            .ToListAsync(cancellationToken);
    }
}
