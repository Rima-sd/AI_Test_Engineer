/*
 * Purpose:
 * Contains the activity functions used by TestGenerationOrchestrator.
 *
 * Responsibilities:
 * 1. Generate or revise xUnit tests through the Foundry Agent.
 * 2. Submit generated tests for validation by an AKS Worker.
 * 3. Retrieve the validation result from Blob Storage.
 * 4. Package the project after successful validation.
 * 5. Raise a failure alert after all five attempts fail.
 *
 * Important:
 * The orchestrator controls the five-attempt Ralph loop.
 * The validation worker performs only one validation attempt.
 */

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TestEngineer.Contracts;
using TestEngineerPOC.Models;
using TestEngineerPOC.Services;

namespace TestEngineerPOC.Function;

public sealed class TestGenerationActivities
{
    private static readonly TimeSpan ResultPollingInterval =
        TimeSpan.FromSeconds(10);

    private static readonly TimeSpan ValidationResultTimeout =
        TimeSpan.FromMinutes(35);

    private readonly ILogger<TestGenerationActivities> _logger;
    private readonly UnitTestGenerationService _unitTestGenerationService;
    private readonly ProjectPackagingService _projectPackagingService;
    private readonly BlobStorageClientFactory _blobStorageClientFactory;
    private readonly AksValidationJobService _aksValidationJobService;

    public TestGenerationActivities(
        ILogger<TestGenerationActivities> logger,
        UnitTestGenerationService unitTestGenerationService,
        ProjectPackagingService projectPackagingService,
        BlobStorageClientFactory blobStorageClientFactory,
        AksValidationJobService aksValidationJobService)
    {
        _logger = logger;
        _unitTestGenerationService = unitTestGenerationService;
        _projectPackagingService = projectPackagingService;
        _blobStorageClientFactory = blobStorageClientFactory;
        _aksValidationJobService = aksValidationJobService;
    }

    /*
     * Activity 1:
     * Generates or revises xUnit tests through the Foundry Agent.
     */
    [Function(nameof(GenerateTestsActivity))]
    public async Task<string> GenerateTestsActivity(
        [ActivityTrigger] TestGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _logger.LogInformation(
            "[RequestId={RequestId}] [Activity=GenerateTests] Starting attempt {AttemptNumber}.",
            context.RequestId,
            context.AttemptNumber);

        var generatedResponse =
            await _unitTestGenerationService.GenerateTestsAsync(context);

        if (string.IsNullOrWhiteSpace(generatedResponse))
        {
            throw new InvalidOperationException(
                $"Foundry returned an empty response for request '{context.RequestId}'.");
        }

        _logger.LogInformation(
            "[RequestId={RequestId}] [Activity=GenerateTests] Generation completed for attempt {AttemptNumber}.",
            context.RequestId,
            context.AttemptNumber);

        return generatedResponse;
    }

    /*
     * Activity 2:
     * Uploads the generated test and validation request to Blob Storage,
     * submits one AKS validation Job, and waits for its result.
     *
     * Returns the validation result together with the exact generated
     * test Blob name and relative file path for successful packaging.
     */
    [Function(nameof(ValidateGeneratedTestsActivity))]
    public async Task<TestGenerationValidationResult> ValidateGeneratedTestsActivity(
        [ActivityTrigger] TestGenerationValidationInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.GenerationContext);

        if (string.IsNullOrWhiteSpace(input.RequestId) ||
            string.IsNullOrWhiteSpace(input.InputContainerName) ||
            string.IsNullOrWhiteSpace(input.InputBlobName))
        {
            throw new ArgumentException(
                "Request ID, container name, and input Blob name are required.");
        }

        if (input.AttemptNumber is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(
                nameof(input.AttemptNumber),
                "Attempt number must be between 1 and 5.");
        }

        _logger.LogInformation(
            "[RequestId={RequestId}] [Activity=ValidateGeneratedTests] Starting attempt {AttemptNumber}.",
            input.RequestId,
            input.AttemptNumber);

        // Parse the Foundry response into a relative file path and C# code.
        var generatedTestFileService = new GeneratedTestFileService();

        var parsedTest =
            generatedTestFileService.ParseResponse(input.GeneratedResponse);

        var safeRequestId = SanitizeBlobSegment(input.RequestId);

        var generatedTestBlobName =
            $"GeneratedTests/{safeRequestId}/attempt-{input.AttemptNumber}/{Guid.NewGuid():N}.cs";

        var requestBlobName =
            $"ValidationRequests/{safeRequestId}/attempt-{input.AttemptNumber}/request.json";

        var resultBlobName =
            $"ValidationResults/{safeRequestId}/attempt-{input.AttemptNumber}/result.json";

        var blobServiceClient =
            await _blobStorageClientFactory.CreateServiceClientAsync(
                cancellationToken);

        var containerClient =
            blobServiceClient.GetBlobContainerClient(
                input.InputContainerName);

        // Upload the generated C# test file.
        var generatedTestBlob =
            containerClient.GetBlobClient(generatedTestBlobName);

        await generatedTestBlob.UploadAsync(
            BinaryData.FromString(parsedTest.Code),
            overwrite: false,
            cancellationToken);

        // Prepare the request for the AKS Validation Worker.
        var validationRequest = new ValidationRequest
        {
            RequestId = input.RequestId,
            AttemptNumber = input.AttemptNumber,
            InputBlobName = input.InputBlobName,
            TargetFilePath = input.GenerationContext.TargetFilePath,
            GeneratedTestFilePath = parsedTest.RelativePath,
            GeneratedTestFileBlobName = generatedTestBlobName,
            ProjectRootPath = string.Empty,
            PreviousDiagnostics =
                input.GenerationContext.PreviousDiagnostics ?? string.Empty
        };

        var requestJson = JsonSerializer.Serialize(
            validationRequest,
            new JsonSerializerOptions
            {
                WriteIndented = true
            });

        var requestBlob =
            containerClient.GetBlobClient(requestBlobName);

        await requestBlob.UploadAsync(
            BinaryData.FromString(requestJson),
            overwrite: false,
            cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] Validation request uploaded. RequestBlob={RequestBlob}",
            input.RequestId,
            requestBlobName);

        // Submit exactly one AKS Job for this attempt.
        var jobName =
            await _aksValidationJobService.SubmitValidationJobAsync(
                input.RequestId,
                input.AttemptNumber,
                input.InputContainerName,
                requestBlobName,
                resultBlobName,
                cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] AKS validation Job submitted. JobName={JobName}",
            input.RequestId,
            jobName);

        // Wait for the Worker to upload the validation result.
        var validationResult = await WaitForValidationResultAsync(
            containerClient,
            resultBlobName,
            input.RequestId,
            input.AttemptNumber,
            cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] Validation completed. " +
            "Attempt={Attempt}; Passed={Passed}; LineCoverage={LineCoverage}; " +
            "BranchCoverage={BranchCoverage}; SurvivingMutants={SurvivingMutants}; " +
            "QualityChecksPassed={QualityChecksPassed}",
            input.RequestId,
            input.AttemptNumber,
            validationResult.Passed,
            validationResult.LineCoveragePercent,
            validationResult.BranchCoveragePercent,
            validationResult.SurvivingMutants,
            validationResult.QualityChecksPassed);

        return new TestGenerationValidationResult
        {
            ValidationResult = validationResult,
            GeneratedTestFileBlobName = generatedTestBlobName,
            GeneratedTestFilePath = parsedTest.RelativePath
        };
    }

    /*
     * Activity 3:
     * Packages the original project with the exact generated test file
     * that passed validation.
     *
     * Uses Blob Storage handoff. No local ProjectRootPath is required.
     */
    [Function(nameof(PackageProjectActivity))]
    public async Task<string> PackageProjectActivity(
        [ActivityTrigger] TestGenerationPackageInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (string.IsNullOrWhiteSpace(input.RequestId) ||
            string.IsNullOrWhiteSpace(input.InputContainerName) ||
            string.IsNullOrWhiteSpace(input.InputBlobName) ||
            string.IsNullOrWhiteSpace(input.GeneratedTestFileBlobName) ||
            string.IsNullOrWhiteSpace(input.GeneratedTestFilePath))
        {
            throw new ArgumentException(
                "Request ID, container, input Blob, generated test Blob, and generated test path are required.",
                nameof(input));
        }

        _logger.LogInformation(
            "[RequestId={RequestId}] Packaging started for successful attempt {AttemptNumber}.",
            input.RequestId,
            input.AttemptNumber);

        var outputBlobName =
            await _projectPackagingService.PackageProjectAsync(
                input.RequestId,
                input.InputContainerName,
                input.InputBlobName,
                input.GeneratedTestFileBlobName,
                input.GeneratedTestFilePath,
                cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] Packaging completed. OutputBlob={OutputBlobName}",
            input.RequestId,
            outputBlobName);

        return outputBlobName;
    }

    /*
     * Activity 4:
     * Logs a critical failure after all five attempts fail.
     *
     * Configure an Azure Monitor Action Group to send an actual
     * email, SMS, or other notification for this event.
     */
    [Function(nameof(RaiseFailureAlertActivity))]
    public Task RaiseFailureAlertActivity(
        [ActivityTrigger] TestGenerationFailureInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        _logger.LogCritical(
            "[AI_TEST_ENGINEER_FAILURE] RequestId={RequestId}; " +
            "Attempts={Attempts}; Diagnostics={Diagnostics}",
            input.RequestId,
            input.Attempts,
            input.Diagnostics);

        return Task.CompletedTask;
    }

    /*
     * Waits for the validation result Blob.
     * Checks RequestId and AttemptNumber to avoid accepting
     * a result belonging to another attempt.
     */
    private async Task<ValidationResult> WaitForValidationResultAsync(
        BlobContainerClient containerClient,
        string resultBlobName,
        string requestId,
        int attemptNumber,
        CancellationToken cancellationToken)
    {
        var resultBlob =
            containerClient.GetBlobClient(resultBlobName);

        var deadline =
            DateTimeOffset.UtcNow.Add(ValidationResultTimeout);

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var exists = await resultBlob.ExistsAsync(
                cancellationToken);

            if (exists.Value)
            {
                var download = await resultBlob.DownloadContentAsync(
                    cancellationToken);

                var json = download.Value.Content.ToString();

                var result = JsonSerializer.Deserialize<ValidationResult>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                if (result is null)
                {
                    throw new InvalidDataException(
                        $"Validation result at '{resultBlobName}' was empty.");
                }

                if (!string.Equals(
                        result.RequestId,
                        requestId,
                        StringComparison.Ordinal) ||
                    result.AttemptNumber != attemptNumber)
                {
                    throw new InvalidDataException(
                        "Validation result does not match the current request and attempt.");
                }

                return result;
            }

            await Task.Delay(
                ResultPollingInterval,
                cancellationToken);
        }

        throw new TimeoutException(
            $"Timed out waiting for AKS validation result. " +
            $"RequestId={requestId}; Attempt={attemptNumber}; " +
            $"ResultBlob={resultBlobName}");
    }

    private static string SanitizeBlobSegment(string value)
    {
        var result = new StringBuilder(value.Length);

        foreach (var character in value)
        {
            result.Append(
                char.IsAsciiLetterOrDigit(character) ||
                character is '-' or '_'
                    ? character
                    : '-');
        }

        var sanitized = result.ToString().Trim('-', '_');

        if (string.IsNullOrWhiteSpace(sanitized))
        {
            throw new ArgumentException(
                "Request ID cannot be converted into a valid Blob path segment.");
        }

        return sanitized.Length > 80
            ? sanitized[..80]
            : sanitized;
    }
}