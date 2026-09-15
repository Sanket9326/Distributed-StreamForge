using Microsoft.AspNetCore.Mvc;
using StreamForge.Engagement.Api.Models;
using StreamForge.Engagement.Api.Services;

namespace StreamForge.Engagement.Api.Controllers;

[ApiController]
[ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
[Route("api/engagement/subscriptions")]
public sealed class SubscriptionsController(SubscriptionService subscriptions) : ControllerBase
{
    [HttpGet]
    public Task<SubscriptionPage> Following([FromQuery] int limit = 20, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => subscriptions.GetPageAsync(UserId(), false, limit, cursor, cancellationToken);

    [HttpGet("subscribers")]
    public Task<SubscriptionPage> Subscribers([FromQuery] int limit = 20, [FromQuery] string? cursor = null,
        CancellationToken cancellationToken = default) => subscriptions.GetPageAsync(UserId(), true, limit, cursor, cancellationToken);

    [HttpGet("status")]
    public Task<IReadOnlyList<SubscriptionStatus>> Status([FromQuery] Guid[] creatorIds, CancellationToken cancellationToken) =>
        subscriptions.GetStatusAsync(UserId(), creatorIds, cancellationToken);

    [HttpPut("{creatorId:guid}")]
    public Task<ActionResult<SubscriptionMutation>> Subscribe(Guid creatorId, CancellationToken cancellationToken) =>
        Change(creatorId, true, false, cancellationToken);

    [HttpDelete("{creatorId:guid}")]
    public Task<ActionResult<SubscriptionMutation>> Unsubscribe(Guid creatorId, CancellationToken cancellationToken) =>
        Change(creatorId, false, false, cancellationToken);

    [HttpDelete("subscribers/{subscriberId:guid}")]
    public Task<ActionResult<SubscriptionMutation>> RemoveSubscriber(Guid subscriberId, CancellationToken cancellationToken) =>
        Change(subscriberId, false, true, cancellationToken);

    private async Task<ActionResult<SubscriptionMutation>> Change(Guid counterpart, bool active, bool incoming, CancellationToken ct) =>
        StatusCode(202, await subscriptions.ChangeAsync(UserId(), counterpart, active, incoming, HttpContext.TraceIdentifier, ct));

    private Guid UserId()
    {
        if (Guid.TryParse(Request.Headers["X-StreamForge-User-Id"].FirstOrDefault(), out var id) && id != Guid.Empty) return id;
        throw new EngagementRequestException(401, "Authentication required", "Please log in.");
    }
}
