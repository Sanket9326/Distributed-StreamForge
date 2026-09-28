using Microsoft.AspNetCore.Mvc;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.Api.Controllers;

[ApiController]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/engagement/watch-history")]
public sealed class WatchHistoryController(WatchHistoryService history) : ControllerBase
{
    [HttpGet]
    public Task<WatchHistoryPage> List([FromQuery] int limit = 20, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => history.GetPageAsync(UserId(), limit, cursor, cancellationToken);
    [HttpGet("{videoId:guid}")]
    public async Task<ActionResult<WatchHistoryItem>> Get(Guid videoId, CancellationToken cancellationToken)
    {
        var result = await history.GetAsync(UserId(), videoId, cancellationToken);
        return result is null ? NoContent() : Ok(result);
    }
    [HttpPut("{videoId:guid}")]
    public async Task<ActionResult<WatchHistoryMutation>> Save(Guid videoId, SaveWatchProgress request, CancellationToken cancellationToken) =>
        StatusCode(202, await history.SaveAsync(UserId(), videoId, request, HttpContext.TraceIdentifier, cancellationToken));
    private Guid UserId()
    {
        if (Guid.TryParse(Request.Headers["X-StreamForge-User-Id"].FirstOrDefault(), out var id) && id != Guid.Empty) return id;
        throw new EngagementRequestException(401, "Authentication required", "Please log in.");
    }
}
