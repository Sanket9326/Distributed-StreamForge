using System.Globalization;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

/// <summary>Coordinates source integrity, FFmpeg encoding, validation, and MinIO publication.</summary>
public sealed class TranscodingPipeline(
    IObjectStorage objectStorage,
    IMediaProbe mediaProbe,
    RenditionSelector renditionSelector,
    GeneratedMediaValidator generatedMediaValidator,
    RenditionKeyFactory keyFactory,
    IVideoEncoder videoEncoder,
    TranscodingTelemetry telemetry,
    IOptions<TranscodingOptions> options,
    ILogger<TranscodingPipeline> logger) : ITranscodingPipeline
{
    private readonly TranscodingOptions transcodingOptions = options.Value;

    public async Task<IReadOnlyList<ProcessedRendition>> ProcessAsync(
        LeasedJob job,
        CancellationToken cancellationToken)
    {
        var workspace = Path.Combine(
            Path.GetFullPath(transcodingOptions.ScratchPath),
            $"{job.EventId:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var uploadedKeys = new List<string>();
        try
        {
            var sourceStat = await objectStorage.StatAsync(
                job.SourceBucket,
                job.SourceObjectKey,
                cancellationToken);
            if (!string.Equals(sourceStat.Etag, job.SourceEtag.Trim('"'), StringComparison.OrdinalIgnoreCase) ||
                sourceStat.SizeBytes != job.SourceSizeBytes)
            {
                throw new PermanentTranscodingException(
                    "source_integrity_mismatch",
                    "The source object no longer matches the accepted upload event.");
            }

            var sourcePath = Path.Combine(workspace, "source.input");
            await objectStorage.DownloadAsync(
                job.SourceBucket,
                job.SourceObjectKey,
                sourcePath,
                cancellationToken);
            var source = await mediaProbe.ProbeSourceAsync(sourcePath, cancellationToken);
            var renditions = renditionSelector.Select(source);
            var completed = new List<ProcessedRendition>(renditions.Count);
            foreach (var rendition in renditions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var outputPath = Path.Combine(workspace, $"{rendition.Tier}.mp4");
                await videoEncoder.EncodeAsync(
                    sourcePath,
                    outputPath,
                    rendition,
                    source.HasAudio,
                    cancellationToken);
                var generated = await mediaProbe.ProbeOutputAsync(outputPath, cancellationToken);
                generatedMediaValidator.Validate(generated, source, rendition);

                var objectKey = keyFactory.Create(job.VideoId, rendition);
                var metadata = CreateMetadata(job, rendition);
                var stored = await objectStorage.UploadRenditionAsync(
                    objectKey,
                    outputPath,
                    metadata,
                    cancellationToken);
                uploadedKeys.Add(objectKey);
                var verified = await objectStorage.StatAsync(
                    stored.Bucket,
                    stored.ObjectKey,
                    cancellationToken);
                if (!string.Equals(stored.Etag, verified.Etag, StringComparison.OrdinalIgnoreCase) ||
                    stored.SizeBytes != verified.SizeBytes)
                {
                    throw new TransientTranscodingException(
                        "rendition_verification_failed",
                        $"Stored rendition {rendition.Tier} could not be verified.");
                }

                completed.Add(new ProcessedRendition(
                    rendition.Tier,
                    rendition.Width,
                    rendition.Height,
                    generated.VideoCodec,
                    generated.HasAudio ? generated.AudioCodec : null,
                    "video/mp4",
                    stored.Bucket,
                    stored.ObjectKey,
                    stored.Etag,
                    stored.SizeBytes));
                telemetry.RenditionSize.Record(
                    stored.SizeBytes,
                    new KeyValuePair<string, object?>("tier", rendition.Tier));
                logger.LogInformation(
                    "Generated {Tier} rendition for video {VideoId} with {SizeBytes} bytes",
                    rendition.Tier,
                    job.VideoId,
                    stored.SizeBytes);
            }

            return completed;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            await DeletePartialRenditionsAsync(uploadedKeys);
            throw;
        }
        finally
        {
            try
            {
                if (Directory.Exists(workspace))
                {
                    Directory.Delete(workspace, recursive: true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "Could not remove transcoding workspace {Workspace}", workspace);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(
        LeasedJob job,
        RenditionDefinition rendition) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["x-amz-meta-video-id"] = job.VideoId.ToString("D"),
            ["x-amz-meta-source-event-id"] = job.EventId.ToString("D"),
            ["x-amz-meta-rendition-tier"] = rendition.Tier,
            ["x-amz-meta-width"] = rendition.Width.ToString(CultureInfo.InvariantCulture),
            ["x-amz-meta-height"] = rendition.Height.ToString(CultureInfo.InvariantCulture),
            ["x-amz-meta-correlation-id"] = job.CorrelationId
        };

    private async Task DeletePartialRenditionsAsync(IEnumerable<string> objectKeys)
    {
        foreach (var objectKey in objectKeys)
        {
            try
            {
                await objectStorage.DeleteRenditionAsync(objectKey, CancellationToken.None);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Could not clean partial rendition {ObjectKey}", objectKey);
            }
        }
    }
}
