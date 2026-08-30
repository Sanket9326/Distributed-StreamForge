namespace StreamForge.Transcoding.Worker.Media;

/// <summary>Builds stable rendition object keys that are safe for idempotent overwrite.</summary>
public sealed class RenditionKeyFactory
{
    public string Create(Guid videoId, RenditionDefinition rendition) =>
        $"videos/{videoId:N}/{rendition.Tier}/{videoId:N}-{rendition.Tier}.mp4";
}
