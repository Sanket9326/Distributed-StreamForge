using Microsoft.Extensions.Options;
using StreamForge.Upload.Api.Options;

namespace StreamForge.Upload.Api.Services;

public sealed class UploadStorage
{
    private readonly string rootPath;

    public UploadStorage(IOptions<UploadStorageOptions> options, IHostEnvironment environment)
    {
        var configuredRoot = options.Value.RootPath;
        rootPath = string.IsNullOrWhiteSpace(configuredRoot)
            ? Path.Combine(Path.GetTempPath(), "streamforge", "uploads")
            : Path.GetFullPath(configuredRoot, environment.ContentRootPath);
    }

    public string RootPath => rootPath;

    public UploadTarget CreateTarget(string extension)
    {
        Directory.CreateDirectory(rootPath);

        var id = Guid.NewGuid();
        return new UploadTarget(
            id,
            Path.Combine(rootPath, $".uploading-{id:N}"),
            Path.Combine(rootPath, $"{id:N}{extension}"));
    }

    public FileStream OpenTemporaryFile(UploadTarget target) =>
        new(
            target.TemporaryPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 81_920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    public void Commit(UploadTarget target) =>
        File.Move(target.TemporaryPath, target.FinalPath);

    public void DeleteTemporaryFile(UploadTarget target)
    {
        if (File.Exists(target.TemporaryPath))
        {
            File.Delete(target.TemporaryPath);
        }
    }

    public async Task VerifyWritableAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(rootPath);
        var probePath = Path.Combine(rootPath, $".health-{Guid.NewGuid():N}");

        await using var probe = new FileStream(
            probePath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1,
            FileOptions.Asynchronous | FileOptions.DeleteOnClose);
        await probe.FlushAsync(cancellationToken);
    }
}

public sealed record UploadTarget(Guid Id, string TemporaryPath, string FinalPath);
