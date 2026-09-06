namespace StreamForge.Engagement.Api.Options;

public sealed class EngagementOptions
{
    public const string SectionName = "Engagement";
    public int ViewAggregationWindowSeconds { get; init; } = 300;
    public int MaximumViewBatchSize { get; init; } = 10_000;
    public int ViewSessionTtlHours { get; init; } = 24;
}
