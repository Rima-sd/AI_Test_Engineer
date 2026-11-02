
namespace TestEngineerWorker.Services;

public interface IBlobProjectDownloader
{
    Task<string> DownloadAsync(
        string containerName,
        string blobName,
        string destinationPath,
        CancellationToken cancellationToken = default);
}
