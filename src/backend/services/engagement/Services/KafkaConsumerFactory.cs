using Confluent.Kafka;

namespace StreamForge.Engagement.Api.Services;

internal static class KafkaConsumerFactory
{
    public static IConsumer<string, string> Create(string bootstrapServers, string groupId, string purpose) =>
        new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = bootstrapServers,
            GroupId = groupId,
            ClientId = $"streamforge-engagement-{purpose}-{Environment.MachineName}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnableAutoOffsetStore = false,
            PartitionAssignmentStrategy = PartitionAssignmentStrategy.CooperativeSticky
        }).Build();
}
