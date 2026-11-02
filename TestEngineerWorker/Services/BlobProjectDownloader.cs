
/*
 * Purpose:
 * Downloads an input project ZIP from Azure Blob Storage.
 *
 * Usage:
 * Uses the configured StorageAccount setting and Azure credentials
 * to retrieve the ZIP for a single validation attempt.
 *
 * Authentication:
 * - Local development: StorageAccount contains the storage connection string.
 * - Azure-hosted environments: StorageAccount contains the HTTPS Blob
 *   Storage endpoint and DefaultAzureCredential is used.
 */

using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Options;
using TestEngineerWorker.Options;

namespace TestEngineerWorker.Services;

public sealed class BlobProjectDownloader : IBlobProjectDownloader
{
    private readonly BlobStorageOptions _blobStorageOptions;

    public BlobProjectDownloader(
        IOptions<BlobStorageOptions> blobStorageOptions)
    {
        _blobStorageOptions = blobStorageOptions?.Value
            ?? throw new ArgumentNullException(nameof(blobStorageOptions));
    }

    public async Task<string> DownloadAsync(
        string containerName,
        string blobName,
        string destinationPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(blobName);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        var serviceClient = CreateBlobServiceClient();

        var blobClient = serviceClient
            .GetBlobContainerClient(containerName)
            .GetBlobClient(blobName);

        var fullDestinationPath = Path.GetFullPath(destinationPath);
        var directory = Path.GetDirectoryName(fullDestinationPath);

        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException(
                "The destination path must include a directory.",
                nameof(destinationPath));
        }

        Directory.CreateDirectory(directory);

        await blobClient.DownloadToAsync(
            fullDestinationPath,
            cancellationToken);

        return fullDestinationPath;
    }

    // ------------------------------------------------------------
    // Creates a BlobServiceClient using the common StorageAccount setting.
    //
    // Local development:
    // StorageAccount contains a connection string.
    //
    // Cloud execution:
    // StorageAccount contains an HTTPS Blob Storage endpoint.
    // DefaultAzureCredential supports AKS Workload Identity when configured.
    // ------------------------------------------------------------
    private BlobServiceClient CreateBlobServiceClient()
    {
        var storageAccount = _blobStorageOptions.StorageAccount;

        if (string.IsNullOrWhiteSpace(storageAccount))
        {
            throw new InvalidOperationException(
                "StorageAccount is not configured.");
        }

        // Local development: use the configured connection string.
        if (storageAccount.StartsWith(
                "DefaultEndpointsProtocol=",
                StringComparison.OrdinalIgnoreCase) ||
            storageAccount.StartsWith(
                "UseDevelopmentStorage=",
                StringComparison.OrdinalIgnoreCase))
        {
            return new BlobServiceClient(storageAccount);
        }

        // Azure-hosted execution: use the HTTPS service endpoint
        // and the configured Azure identity.
        if (!Uri.TryCreate(
                storageAccount,
                UriKind.Absolute,
                out var serviceUri) ||
            serviceUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "StorageAccount must contain a valid connection string " +
                "or an HTTPS Blob Storage endpoint.");
        }

        return new BlobServiceClient(
            serviceUri,
            new DefaultAzureCredential());
    }
}
