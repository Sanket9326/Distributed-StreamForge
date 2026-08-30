using StreamForge.Transcoding.Worker.Options;
using StreamForge.Transcoding.Worker.Services;

namespace StreamForge.Transcoding.UnitTests;

public sealed class KafkaOutboxPublisherTests
{
    private readonly KafkaOptions options = new()
    {
        InputTopic = "video-processing",
        CompletedTopic = "video-transcoding-completed",
        FailedTopic = "video-transcoding-failed",
        DeadLetterTopic = "video-processing-dead-letter"
    };

    [Fact]
    public void EnsureOwnedOutputTopic_InputTopic_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            KafkaOutboxPublisher.EnsureOwnedOutputTopic("video-processing", options));

        Assert.Contains("not allowed", exception.Message);
    }

    [Theory]
    [InlineData("video-transcoding-completed")]
    [InlineData("video-transcoding-failed")]
    [InlineData("video-processing-dead-letter")]
    public void EnsureOwnedOutputTopic_ServiceOwnedTopic_Succeeds(string topic)
    {
        KafkaOutboxPublisher.EnsureOwnedOutputTopic(topic, options);
    }
}
