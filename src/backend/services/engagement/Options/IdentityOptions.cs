namespace StreamForge.Engagement.Api.Options;
public sealed class IdentityOptions
{
    public const string SectionName = "Identity";
    public string BaseUrl { get; init; } = "http://localhost:5084";
}
