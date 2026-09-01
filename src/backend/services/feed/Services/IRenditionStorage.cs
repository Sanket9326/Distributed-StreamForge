namespace StreamForge.Feed.Api.Services;

public interface IRenditionStorage
{
    Task VerifyAvailableAsync(CancellationToken cancellationToken);
}
