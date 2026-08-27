using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Options;
using StreamForge.Upload.Api.Services;

namespace StreamForge.Upload.UnitTests;

public sealed class UploadStorageTests : IDisposable
{
    private readonly string rootPath = Path.Combine(
        Path.GetTempPath(),
        "streamforge-tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void CreateTarget_GeneratesOpaqueNamesInsideStorageRoot()
    {
        var storage = CreateStorage();

        var first = storage.CreateTarget(".mp4");
        var second = storage.CreateTarget(".mp4");

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(rootPath, Path.GetDirectoryName(first.FinalPath));
        Assert.Equal($"{first.Id:N}.mp4", Path.GetFileName(first.FinalPath));
        Assert.StartsWith(".uploading-", Path.GetFileName(first.TemporaryPath));
    }

    [Fact]
    public async Task TemporaryFile_CanBeRemovedAfterAnInterruptedWrite()
    {
        var storage = CreateStorage();
        var target = storage.CreateTarget(".webm");

        await using (var stream = storage.OpenTemporaryFile(target))
        {
            await stream.WriteAsync("partial"u8.ToArray());
        }

        storage.DeleteTemporaryFile(target);

        Assert.False(File.Exists(target.TemporaryPath));
        Assert.False(File.Exists(target.FinalPath));
    }

    public void Dispose()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

    private UploadStorage CreateStorage() =>
        new(
            Options.Create(new UploadStorageOptions { RootPath = rootPath }),
            new TestHostEnvironment(rootPath));

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "StreamForge.Upload.UnitTests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
