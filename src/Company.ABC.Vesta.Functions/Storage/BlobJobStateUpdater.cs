// Purpose: Implements job state persistence and audit logging using Azure Blob Storage.

using System.Text.Json;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Functions.Configuration;

namespace Company.ABC.Vesta.Functions.Storage;

public sealed class BlobJobStateUpdater : IJobStateUpdater
{
    private readonly BlobContainerClient _jobsContainer;
    private readonly JsonSerializerOptions _jsonOptions;

    public BlobJobStateUpdater(
        BlobServiceClients clients,
        VestaContainers containers)
    {
        _jobsContainer = clients.Vesta.GetBlobContainerClient(containers.Jobs);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
    }

    public async Task UpdateStateAsync(JobDocument jobDocument, CancellationToken cancellationToken)
    {
        var blobClient = _jobsContainer.GetBlobClient($"{jobDocument.JobId}/job.json");

        var json = JsonSerializer.Serialize(jobDocument, _jsonOptions);
        await blobClient.UploadAsync(new BinaryData(json), overwrite: true, cancellationToken);
    }

    public async Task WriteAuditAsync(string jobId, AuditEntry entry, CancellationToken cancellationToken)
    {
        // Using AppendBlobClient to safely append immutable logs without overwriting
        var appendBlobClient = _jobsContainer.GetAppendBlobClient($"{jobId}/audit.log");

        await appendBlobClient.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var json = JsonSerializer.Serialize(entry, _jsonOptions) + Environment.NewLine;

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(json));
        await appendBlobClient.AppendBlockAsync(stream, cancellationToken: cancellationToken);
    }
}