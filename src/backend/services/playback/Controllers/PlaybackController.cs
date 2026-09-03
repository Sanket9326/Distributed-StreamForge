using Microsoft.AspNetCore.Mvc; using Microsoft.EntityFrameworkCore; using Minio.Exceptions; using StreamForge.Playback.Api.Data; using StreamForge.Playback.Api.Services;
namespace StreamForge.Playback.Api.Controllers;
[ApiController][Route("api/playback/videos/{videoId:guid}")]
public sealed class PlaybackController(IDbContextFactory<PlaybackDbContext> factory,IManifestStorage storage,ManifestRewriter rewriter,PlaybackTelemetry telemetry):ControllerBase
{
    [HttpGet("master.m3u8")] public async Task<IActionResult> Master(Guid videoId,CancellationToken token)
    {telemetry.ManifestRequests.Add(1);var package=await Find(videoId,token);if(package is null)return Unavailable();try{return Manifest(rewriter.RewriteMaster(await storage.ReadAsync(package.Bucket,package.MasterPlaylistObjectKey,token),package));}catch(InvalidManifestException){telemetry.InvalidManifests.Add(1);return StatusCode(500);}catch(Exception ex)when(StorageError(ex)){telemetry.StorageFailures.Add(1);return Unavailable();}}
    [HttpGet("variants/{tier}.m3u8")] public async Task<IActionResult> Variant(Guid videoId,string tier,CancellationToken token)
    {telemetry.ManifestRequests.Add(1);var package=await Find(videoId,token);if(package is null)return Unavailable();var variant=package.Variants.SingleOrDefault(x=>x.Tier==tier);if(variant is null)return NotFound();try{var raw=await storage.ReadAsync(package.Bucket,variant.PlaylistObjectKey,token);var rewritten=await rewriter.RewriteVariantAsync(raw,package,variant,async key=>{telemetry.SigningOperations.Add(1);return await storage.SignAsync(package.Bucket,key,token);});return Manifest(rewritten);}catch(InvalidManifestException){telemetry.InvalidManifests.Add(1);return StatusCode(500);}catch(Exception ex)when(StorageError(ex)){telemetry.StorageFailures.Add(1);return Unavailable();}}
    private async Task<PlaybackPackage?> Find(Guid id,CancellationToken token){await using var db=await factory.CreateDbContextAsync(token);return await db.Packages.AsNoTracking().Include(x=>x.Variants).SingleOrDefaultAsync(x=>x.VideoId==id,token);}
    private ContentResult Manifest(string body){Response.Headers.CacheControl="private, no-store";return Content(body,"application/vnd.apple.mpegurl",System.Text.Encoding.UTF8);}
    private ObjectResult Unavailable(){telemetry.MissingProjections.Add(1);Response.Headers.RetryAfter="1";return StatusCode(StatusCodes.Status503ServiceUnavailable,new ProblemDetails{Title="Playback temporarily unavailable",Status=503});}
    private static bool StorageError(Exception ex)=>ex is MinioException or HttpRequestException or IOException;
}
