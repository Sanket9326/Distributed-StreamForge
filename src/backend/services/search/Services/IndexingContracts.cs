namespace StreamForge.Search.Api.Services;

public sealed record SearchConsumedEnvelope(
    string Topic,
    int Partition,
    long Offset,
    string? Key,
    string Payload);

public enum IndexingHandleResult
{
    Completed,
    Retry
}
