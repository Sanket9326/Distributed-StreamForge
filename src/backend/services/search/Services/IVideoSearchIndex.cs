using StreamForge.Search.Api.Models;

namespace StreamForge.Search.Api.Services;

public interface IVideoSearchIndex
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken);
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
    Task<IndexWriteResult> IndexAsync(VideoSearchIndexRequestedV1 requested, CancellationToken cancellationToken);
    Task<IReadOnlyList<VideoSuggestion>> SuggestAsync(string query, int limit, CancellationToken cancellationToken);
}

public enum IndexWriteResult
{
    Indexed,
    Superseded,
    TransientFailure,
    PermanentFailure
}
