/*
 * Purpose:
 * Bridges the Function App to one AKS validation attempt.
 * Persists request/result messages in Blob Storage, submits the AKS Job,
 * waits for its result, and verifies the result belongs to this attempt.
 * The caller owns the five-attempt Foundry/Ralph loop.
 */
using System.Text.Json;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using TestEngineer.Contracts;
using TestEngineerPOC.Services;

namespace TestEngineerPOC.Services;

public sealed class ValidationOrchestrationService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan ResultTimeout = TimeSpan.FromMinutes(32);
    private readonly BlobStorageClientFactory _blobFactory;
    private readonly AksValidationJobService _aks;
    private readonly ILogger<ValidationOrchestrationService> _logger;

    public ValidationOrchestrationService(
        BlobStorageClientFactory blobFactory,
        AksValidationJobService aks,
        ILogger<ValidationOrchestrationService> logger)
    {
        _blobFactory = blobFactory;
        _aks = aks;
        _logger = logger;
    }

    public async Task<ValidationResult> ValidateAsync(
        ValidationRequest request,
        string containerName,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.RequestId))
            throw new ArgumentException("RequestId is required.", nameof(request));
        if (request.AttemptNumber is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(request.AttemptNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        var safeId = new string(request.RequestId.ToLowerInvariant()
            .Select(c => char.IsAsciiLetterOrDigit(c) ? c : '-')
            .ToArray()).Trim('-');
        var nonce = Guid.NewGuid().ToString("N");
        var requestBlobName = $"ValidationRequests/{safeId}/attempt-{request.AttemptNumber}-{nonce}.json";
        var resultBlobName = $"ValidationResults/{safeId}/attempt-{request.AttemptNumber}-{nonce}.json";

        var serviceClient = await _blobFactory.CreateServiceClientAsync(cancellationToken);
        var container = serviceClient.GetBlobContainerClient(containerName);
        var requestBlob = container.GetBlobClient(requestBlobName);
        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var requestJson = JsonSerializer.Serialize(request, jsonOptions);
        await requestBlob.UploadAsync(BinaryData.FromString(requestJson), overwrite: false, cancellationToken);

        var jobName = await _aks.SubmitValidationJobAsync(
            request.RequestId,
            request.AttemptNumber,
            containerName,
            requestBlobName,
            resultBlobName,
            cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=ValidationJobSubmitted] Attempt={Attempt}; Job={JobName}; ResultBlob={ResultBlob}",
            request.RequestId, request.AttemptNumber, jobName, resultBlobName);

        var resultBlob = container.GetBlobClient(resultBlobName);
        var deadline = DateTimeOffset.UtcNow.Add(ResultTimeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var exists = await resultBlob.ExistsAsync(cancellationToken);
            if (exists.Value)
            {
                var downloaded = await resultBlob.DownloadContentAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<ValidationResult>(
                    downloaded.Value.Content.ToString(), jsonOptions)
                    ?? throw new JsonException("AKS worker returned an empty ValidationResult.");

                if (!string.Equals(result.RequestId, request.RequestId, StringComparison.Ordinal) ||
                    result.AttemptNumber != request.AttemptNumber)
                    throw new InvalidDataException("Validation result RequestId/AttemptNumber does not match the submitted request.");

                _logger.LogInformation(
                    "[RequestId={RequestId}] [Stage=ValidationResultReceived] Attempt={Attempt}; Passed={Passed}; Line={LineCoverage}; Branch={BranchCoverage}; Mutants={Mutants}; Quality={Quality}",
                    result.RequestId, result.AttemptNumber, result.Passed,
                    result.LineCoveragePercent, result.BranchCoveragePercent,
                    result.SurvivingMutants, result.QualityChecksPassed);
                return result;
            }
            await Task.Delay(PollInterval, cancellationToken);
        }

        throw new TimeoutException(
            $"AKS validation Job '{jobName}' did not publish result '{resultBlobName}' within {ResultTimeout}.");
    }
}
