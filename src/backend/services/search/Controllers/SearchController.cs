using Microsoft.AspNetCore.Mvc;
using StreamForge.Search.Api.Models;
using StreamForge.Search.Api.Services;

namespace StreamForge.Search.Api.Controllers;

[ApiController]
[Route("api/search/videos")]
public sealed class SearchController(IVideoSearchIndex searchIndex) : ControllerBase
{
    [HttpGet("suggestions")]
    [ProducesResponseType<VideoSuggestionsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<VideoSuggestionsResponse>> Suggestions(
        [FromQuery] string? q,
        [FromQuery] int limit = 8,
        CancellationToken cancellationToken = default)
    {
        var query = q?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length > 200)
        {
            ModelState.AddModelError(nameof(q), "q must contain between 1 and 200 characters.");
        }
        if (limit is < 1 or > 20)
        {
            ModelState.AddModelError(nameof(limit), "limit must be between 1 and 20.");
        }
        if (!ModelState.IsValid)
        {
            return ValidationProblem(ModelState);
        }

        var items = await searchIndex.SuggestAsync(query!, limit, cancellationToken);
        return Ok(new VideoSuggestionsResponse(items));
    }
}
