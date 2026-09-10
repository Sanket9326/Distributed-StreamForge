using StreamForge.Search.Api.Models;

namespace StreamForge.Search.Api.Services;

public interface ISearchDeadLetterPublisher
{
    Task PublishAsync(
        SearchIndexDeadLetterV1 deadLetter,
        string partitionKey,
        CancellationToken cancellationToken);
}
