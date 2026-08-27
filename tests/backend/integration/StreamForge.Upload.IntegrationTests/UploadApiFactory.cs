using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace StreamForge.Upload.IntegrationTests;

public sealed class UploadApiFactory : WebApplicationFactory<Program>
{
    public UploadApiFactory()
    {
        StorageRoot = Path.Combine(
            Path.GetTempPath(),
            "streamforge-tests",
            Guid.NewGuid().ToString("N"));
    }

    public string StorageRoot { get; }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
        });
        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UploadStorage:RootPath"] = StorageRoot,
                ["UploadStorage:MaxFileSizeBytes"] = "8"
            });
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing && Directory.Exists(StorageRoot))
        {
            Directory.Delete(StorageRoot, recursive: true);
        }
    }
}
