// Purpose: Implements write-once storage for completed Vesta output ZIP files using Azure Blob Storage.

using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Functions.Storage;

public sealed class BlobOutputStore : IOutputStore
{
    private readonly BlobContainerClient _outputContainer;

    public BlobOutputStore(
        BlobServiceClients clients,
        VestaContainers containers)
    {
        _outputContainer = clients.Vesta.GetBlobContainerClient(containers.Output);
    }

    public async Task<string> WriteNewZipAsync(
        string jobId,
        string fullName,
        ReadOnlyMemory<byte> zipContent,
        CancellationToken cancellationToken = default)
    {
        // Using JobPaths pattern for output ZIP storage
        var blobPath = JobPaths.OutputZip(jobId, 1);
        var blobClient = _outputContainer.GetBlobClient(blobPath);

        // Enforce write-once semantics: fail if the output blob already exists
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders
            {
                ContentType = StorageConstants.ZipContentType
            },
            Conditions = new BlobRequestConditions
            {
                IfNoneMatch = ETag.All
            }
        };

        using var stream = new MemoryStream(zipContent.ToArray());
        await blobClient.UploadAsync(stream, options, cancellationToken).ConfigureAwait(false);

        return blobPath;
    }
}