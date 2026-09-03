using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;
using StreamForge.Transcoding.Worker.Media;
using StreamForge.Transcoding.Worker.Options;

namespace StreamForge.Transcoding.Worker.Services;

public sealed class TranscodingPipeline(
    IObjectStorage objectStorage, IMediaProbe mediaProbe, RenditionSelector renditionSelector,
    GeneratedMediaValidator generatedMediaValidator, RenditionKeyFactory keyFactory,
    HlsObjectKeyFactory hlsKeyFactory, IVideoEncoder videoEncoder, IHlsPackager hlsPackager,
    HlsPackageValidator hlsValidator, HlsManifestBuilder manifestBuilder, TranscodingTelemetry telemetry,
    IOptions<TranscodingOptions> options, ILogger<TranscodingPipeline> logger) : ITranscodingPipeline
{
    private readonly TranscodingOptions transcodingOptions = options.Value;

    public async Task<ProcessedTranscodingResult> ProcessAsync(LeasedJob job, CancellationToken cancellationToken)
    {
        var workspace = Path.Combine(Path.GetFullPath(transcodingOptions.ScratchPath), $"{job.EventId:N}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(workspace);
        var uploadedKeys = new ConcurrentBag<string>();
        try
        {
            var sourceStat = await objectStorage.StatAsync(job.SourceBucket, job.SourceObjectKey, cancellationToken);
            if (!string.Equals(sourceStat.Etag, job.SourceEtag.Trim('"'), StringComparison.OrdinalIgnoreCase) || sourceStat.SizeBytes != job.SourceSizeBytes)
                throw new PermanentTranscodingException("source_integrity_mismatch", "The source object no longer matches the accepted upload event.");

            var sourcePath = Path.Combine(workspace, "source.input");
            await objectStorage.DownloadAsync(job.SourceBucket, job.SourceObjectKey, sourcePath, cancellationToken);
            var source = await mediaProbe.ProbeSourceAsync(sourcePath, cancellationToken);
            var progressive = new List<ProcessedRendition>();
            var hlsVariants = new List<ProcessedHlsVariant>();
            var packagedDurationSeconds = 0d;

            foreach (var rendition in renditionSelector.Select(source))
            {
                var outputPath = Path.Combine(workspace, $"{rendition.Tier}.mp4");
                await videoEncoder.EncodeAsync(sourcePath, outputPath, rendition, source.HasAudio, cancellationToken);
                var generated = await mediaProbe.ProbeOutputAsync(outputPath, cancellationToken);
                generatedMediaValidator.Validate(generated, source, rendition);
                var storedMp4 = await UploadVerifiedAsync(keyFactory.Create(job.VideoId, rendition), outputPath, "video/mp4", CreateMetadata(job, rendition), uploadedKeys, cancellationToken);
                progressive.Add(new(rendition.Tier, rendition.Width, rendition.Height, generated.VideoCodec,
                    generated.HasAudio ? generated.AudioCodec : null, "video/mp4", storedMp4.Bucket, storedMp4.ObjectKey, storedMp4.Etag, storedMp4.SizeBytes));

                var tierDirectory = Path.Combine(workspace, rendition.Tier);
                var timer = Stopwatch.StartNew();
                await hlsPackager.PackageAsync(outputPath, tierDirectory, cancellationToken);
                ValidatedHlsVariant validation;
                try { validation = hlsValidator.Validate(tierDirectory); }
                catch (PermanentTranscodingException) { telemetry.ValidationFailures.Add(1, new KeyValuePair<string, object?>("asset", "playlist")); throw; }
                packagedDurationSeconds = Math.Max(packagedDurationSeconds, validation.Duration.TotalSeconds);
                telemetry.PackagingDuration.Record(timer.Elapsed.TotalSeconds, new KeyValuePair<string, object?>("tier", rendition.Tier));
                telemetry.SegmentCount.Record(validation.SegmentCount, new KeyValuePair<string, object?>("tier", rendition.Tier));
                telemetry.PackagedBytes.Record(validation.SizeBytes, new KeyValuePair<string, object?>("tier", rendition.Tier));
                var files = Directory.GetFiles(tierDirectory).OrderBy(path => Path.GetFileName(path) == "index.m3u8" ? 1 : 0).ToArray();
                var storedFiles = await UploadBoundedAsync(files, path => hlsKeyFactory.Asset(job.VideoId, rendition.Tier, Path.GetFileName(path)), job, rendition, uploadedKeys, cancellationToken);
                var playlist = storedFiles.Single(item => item.ObjectKey.EndsWith("/index.m3u8", StringComparison.Ordinal));
                hlsVariants.Add(new(rendition.Tier, rendition.Width, rendition.Height, rendition.FrameRate, "h264",
                    generated.HasAudio ? "aac" : null, generated.HasAudio ? "avc1.4d401f,mp4a.40.2" : "avc1.4d401f",
                    validation.BandwidthBitsPerSecond, validation.AverageBandwidthBitsPerSecond,
                    playlist.ObjectKey, playlist.Etag, validation.SegmentCount, validation.SizeBytes));
                File.Delete(outputPath);
                Directory.Delete(tierDirectory, true);
            }

            var masterPath = Path.Combine(workspace, "master.m3u8");
            await File.WriteAllTextAsync(masterPath, manifestBuilder.BuildMaster(hlsVariants), cancellationToken);
            var master = await UploadVerifiedAsync(hlsKeyFactory.Master(job.VideoId), masterPath, "application/vnd.apple.mpegurl",
                new Dictionary<string, string> { ["x-amz-meta-video-id"] = job.VideoId.ToString("D") }, uploadedKeys, cancellationToken);
            return new(progressive, new(objectStorage.RenditionsBucket, hlsKeyFactory.Prefix(job.VideoId), master.ObjectKey,
                master.Etag, "fmp4", transcodingOptions.HlsSegmentDurationSeconds, packagedDurationSeconds,
                hlsVariants.Sum(value => value.SizeBytes) + master.SizeBytes, hlsVariants));
        }
        catch (OperationCanceledException) { throw; }
        catch { await DeletePartialAsync(uploadedKeys); throw; }
        finally
        {
            try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { logger.LogWarning(exception, "Could not remove transcoding workspace {Workspace}", workspace); }
        }
    }

    private async Task<IReadOnlyList<StoredObjectInfo>> UploadBoundedAsync(string[] paths, Func<string, string> key, LeasedJob job,
        RenditionDefinition rendition, ConcurrentBag<string> uploadedKeys, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(transcodingOptions.AssetUploadConcurrency);
        var tasks = paths.Select(async path =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var contentType = Path.GetExtension(path) switch { ".m3u8" => "application/vnd.apple.mpegurl", ".m4s" => "video/iso.segment", _ => "video/mp4" };
                return await UploadVerifiedAsync(key(path), path, contentType, CreateMetadata(job, rendition), uploadedKeys, cancellationToken);
            }
            catch { telemetry.UploadFailures.Add(1, new KeyValuePair<string, object?>("asset", Path.GetExtension(path))); throw; }
            finally { semaphore.Release(); }
        });
        return await Task.WhenAll(tasks);
    }

    private async Task<StoredObjectInfo> UploadVerifiedAsync(string key, string path, string contentType,
        IReadOnlyDictionary<string, string> metadata, ConcurrentBag<string> uploadedKeys, CancellationToken cancellationToken)
    {
        var stored = await objectStorage.UploadAssetAsync(key, path, contentType, metadata, cancellationToken);
        uploadedKeys.Add(key);
        var verified = await objectStorage.StatAsync(stored.Bucket, stored.ObjectKey, cancellationToken);
        if (!string.Equals(stored.Etag, verified.Etag, StringComparison.OrdinalIgnoreCase) || stored.SizeBytes != verified.SizeBytes)
        {
            telemetry.ValidationFailures.Add(1, new KeyValuePair<string, object?>("asset", Path.GetExtension(path)));
            throw new TransientTranscodingException("asset_verification_failed", "A stored transcoding asset could not be verified.");
        }
        return stored;
    }

    private static IReadOnlyDictionary<string, string> CreateMetadata(LeasedJob job, RenditionDefinition rendition) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["x-amz-meta-video-id"] = job.VideoId.ToString("D"), ["x-amz-meta-source-event-id"] = job.EventId.ToString("D"),
            ["x-amz-meta-rendition-tier"] = rendition.Tier, ["x-amz-meta-width"] = rendition.Width.ToString(CultureInfo.InvariantCulture),
            ["x-amz-meta-height"] = rendition.Height.ToString(CultureInfo.InvariantCulture), ["x-amz-meta-correlation-id"] = job.CorrelationId };

    private async Task DeletePartialAsync(IEnumerable<string> keys)
    {
        foreach (var key in keys.Distinct(StringComparer.Ordinal))
            try { await objectStorage.DeleteRenditionAsync(key, CancellationToken.None); }
            catch (Exception exception) { logger.LogError(exception, "Could not clean partial asset {ObjectKey}", key); }
    }
}
