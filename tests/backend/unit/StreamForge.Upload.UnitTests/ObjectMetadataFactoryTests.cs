using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class ObjectMetadataFactoryTests
{
    [Fact]
    public void Create_EncodesFileNameAndIncludesSafeIdentifiers()
    {
        var videoId = Guid.Parse("fb79f32a-f61e-48a2-a560-eaed870ea40c");
        var ownerId = Guid.Parse("fa969552-d425-4c57-bb8c-a42e90607d70");
        var upload = new ObjectUpload(
            videoId,
            "sources/key.mp4",
            "source video ☃.mp4",
            "video/mp4",
            new DateTimeOffset(2026, 8, 29, 10, 30, 0, TimeSpan.Zero),
            "correlation-123",
            ownerId,
            Stream.Null);

        var metadata = new ObjectMetadataFactory().Create(upload);

        Assert.Equal(videoId.ToString("D"), metadata["x-amz-meta-video-id"]);
        Assert.Equal("2026-08-29T10:30:00.0000000+00:00", metadata["x-amz-meta-uploaded-at-utc"]);
        Assert.Equal("correlation-123", metadata["x-amz-meta-correlation-id"]);
        Assert.Equal("source%20video%20%E2%98%83.mp4", metadata["x-amz-meta-original-file-name"]);
        Assert.Equal(ownerId.ToString("D"), metadata["x-amz-meta-owner-id"]);
    }

    [Fact]
    public void Create_OmitsOwnerMetadataForAnonymousUpload()
    {
        var upload = new ObjectUpload(
            Guid.NewGuid(),
            "sources/key.mp4",
            "source.mp4",
            "video/mp4",
            DateTimeOffset.UtcNow,
            "correlation-123",
            OwnerId: null,
            Stream.Null);

        var metadata = new ObjectMetadataFactory().Create(upload);

        Assert.DoesNotContain("x-amz-meta-owner-id", metadata.Keys);
    }
}
