using System.Text.Json; using Microsoft.EntityFrameworkCore; using StreamForge.Playback.Api.Data; using StreamForge.Playback.Api.Models;
namespace StreamForge.Playback.Api.Services;
public sealed record ConsumedEnvelope(string Topic,int Partition,long Offset,string Payload);
public sealed class CompletionProjector(IDbContextFactory<PlaybackDbContext> factory,TimeProvider clock)
{
    private static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    public async Task ProjectAsync(ConsumedEnvelope envelope,CancellationToken token)
    {
        await using var db=await factory.CreateDbContextAsync(token);if(await db.ConsumedMessages.AnyAsync(x=>x.Topic==envelope.Topic&&x.Partition==envelope.Partition&&x.Offset==envelope.Offset,token))return;
        Guid? eventId=null;string? rejection=null;VideoTranscodingCompletedV2? completed=null;
        try{using var doc=JsonDocument.Parse(envelope.Payload);eventId=doc.RootElement.TryGetProperty("eventId",out var id)&&id.TryGetGuid(out var parsed)?parsed:null;var version=doc.RootElement.TryGetProperty("eventVersion",out var v)?v.GetInt32():0;if(version==2)completed=JsonSerializer.Deserialize<VideoTranscodingCompletedV2>(envelope.Payload,Json);else if(version!=1)rejection="unsupported_version";}
        catch(JsonException){rejection="malformed_json";}
        await using var tx=await db.Database.BeginTransactionAsync(token);
        if(eventId is not null&&await db.ConsumedMessages.AnyAsync(x=>x.EventId==eventId,token)){eventId=null;rejection="duplicate_event";completed=null;}
        if(completed is not null)
        {
            if(!Valid(completed)){rejection="invalid_completed_event";} else
            {
                var existing=await db.Packages.Include(x=>x.Variants).SingleOrDefaultAsync(x=>x.VideoId==completed.VideoId,token);
                var package=existing??new PlaybackPackage{VideoId=completed.VideoId};if(existing is null)db.Packages.Add(package);else db.Variants.RemoveRange(existing.Variants);
                package.Bucket=completed.HlsPackage.Bucket;package.AssetPrefix=completed.HlsPackage.AssetPrefix;package.MasterPlaylistObjectKey=completed.HlsPackage.MasterPlaylistObjectKey;package.MasterPlaylistEtag=completed.HlsPackage.MasterPlaylistEtag;package.ProjectedAtUtc=clock.GetUtcNow();package.Variants=completed.HlsPackage.Variants.Select(x=>new PlaybackVariant{VideoId=completed.VideoId,Tier=x.Tier,Width=x.Width,Height=x.Height,BandwidthBitsPerSecond=x.BandwidthBitsPerSecond,PlaylistObjectKey=x.PlaylistObjectKey,PlaylistEtag=x.PlaylistEtag}).ToList();
            }
        }
        db.ConsumedMessages.Add(new ConsumedMessage{Topic=envelope.Topic,Partition=envelope.Partition,Offset=envelope.Offset,EventId=eventId,ConsumedAtUtc=clock.GetUtcNow(),RejectionCode=rejection});await db.SaveChangesAsync(token);await tx.CommitAsync(token);
    }
    private static bool Valid(VideoTranscodingCompletedV2 value){var package=value.HlsPackage;if(value.EventType!="video.transcoding.completed"||value.EventVersion!=2||value.EventId==Guid.Empty||value.VideoId==Guid.Empty||package is null||package.Variants is null||package.Variants.Count==0)return false;var prefix=$"videos/{value.VideoId:N}/hls/";return package.AssetPrefix==prefix&&package.MasterPlaylistObjectKey==prefix+"master.m3u8"&&package.Variants.Select(x=>x.Tier).Distinct(StringComparer.Ordinal).Count()==package.Variants.Count&&package.Variants.All(x=>x.PlaylistObjectKey==prefix+x.Tier+"/index.m3u8");}
}
