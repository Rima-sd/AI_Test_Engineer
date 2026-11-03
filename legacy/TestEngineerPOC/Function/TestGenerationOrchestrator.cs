/*
 * Purpose:
 * Controls the AI Test Engineer workflow using Durable Functions.
 *
 * Responsibilities:
 * 1. Generate xUnit tests using the Foundry Agent activity.
 * 2. Validate the generated tests through the AKS validation activity.
 * 3. Retry generation with validation diagnostics, up to five attempts.
 * 4. Package the project only after all validation checks pass.
 * 5. Raise an alert when all five attempts fail.
 *
 * Important:
 * The orchestrator coordinates activities only. It must not directly
 * call Azure services, Foundry, Blob Storage, or AKS.
 */

using System;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.DurableTask;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Models;
using TestEngineer.Contracts;

namespace TestEngineerPOC.Function;

public sealed class TestGenerationOrchestrator
{
    private const int MaxAttempts = 5;

    [Function(nameof(TestGenerationOrchestrator))]
    public async Task<string> Run(
        [OrchestrationTrigger] TaskOrchestrationContext context)
    {
        var logger = context.CreateReplaySafeLogger(
            nameof(TestGenerationOrchestrator));

        var input = context.GetInput<TestGenerationOrchestrationInput>();

        if (input?.GenerationContext is null)
        {
            throw new InvalidOperationException(
                "Test generation orchestration input is missing.");
        }

        var generationContext = input.GenerationContext;
        var requestId = generationContext.RequestId;

        string previousDiagnostics =
            generationContext.PreviousDiagnostics ?? string.Empty;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Pass the current attempt and previous validation feedback
            // to the Foundry Agent.
            generationContext.AttemptNumber = attempt;
            generationContext.PreviousDiagnostics = previousDiagnostics;

            logger.LogInformation(
                "[RequestId={RequestId}] Starting attempt {Attempt}/{MaxAttempts}.",
                requestId,
                attempt,
                MaxAttempts);

            // Activity 1: Generate or revise xUnit tests using Foundry.
            var generatedResponse = await context.CallActivityAsync<string>(
                nameof(TestGenerationActivities.GenerateTestsActivity),
                generationContext);

            if (string.IsNullOrWhiteSpace(generatedResponse))
            {
                previousDiagnostics =
                    "The Foundry Agent returned an empty test-generation response.";

                continue;
            }

            // Activity 2: Validate the generated tests using AKS.
            // The activity returns validation metrics and the generated
            // test Blob details for the current attempt.
            var validationOutput =
                await context.CallActivityAsync<TestGenerationValidationResult>(
                    nameof(TestGenerationActivities.ValidateGeneratedTestsActivity),
                    new TestGenerationValidationInput
                    {
                        RequestId = requestId,
                        AttemptNumber = attempt,
                        GeneratedResponse = generatedResponse,
                        GenerationContext = generationContext,
                        InputContainerName = input.InputContainerName,
                        InputBlobName = input.InputBlobName
                    });

            if (validationOutput?.ValidationResult is null)
            {
                previousDiagnostics =
                    "The validation activity returned no validation result.";

                continue;
            }

            var validationResult = validationOutput.ValidationResult;

            if (validationResult.Passed)
            {
                logger.LogInformation(
                    "[RequestId={RequestId}] Validation passed on attempt {Attempt}.",
                    requestId,
                    attempt);

                // Activity 3: Package the original project with the exact
                // generated test file that passed this validation attempt.
                var outputBlobName = await context.CallActivityAsync<string>(
                    nameof(TestGenerationActivities.PackageProjectActivity),
                    new TestGenerationPackageInput
                    {
                        RequestId = requestId,
                        InputContainerName = input.InputContainerName,
                        InputBlobName = input.InputBlobName,
                        GeneratedTestFileBlobName =
                            validationOutput.GeneratedTestFileBlobName,
                        GeneratedTestFilePath =
                            validationOutput.GeneratedTestFilePath,
                        AttemptNumber = attempt
                    });

                logger.LogInformation(
                    "[RequestId={RequestId}] Output package created: {OutputBlobName}",
                    requestId,
                    outputBlobName);

                return $"SUCCESS: Request {requestId} passed validation on attempt {attempt}.";
            }

            previousDiagnostics = validationResult.Diagnostics;

            logger.LogWarning(
                "[RequestId={RequestId}] Attempt {Attempt} failed validation. Diagnostics: {Diagnostics}",
                requestId,
                attempt,
                previousDiagnostics);
        }

        // All five attempts failed. Do not package or publish the project.
        await context.CallActivityAsync(
            nameof(TestGenerationActivities.RaiseFailureAlertActivity),
            new TestGenerationFailureInput
            {
                RequestId = requestId,
                Attempts = MaxAttempts,
                Diagnostics = previousDiagnostics
            });

        return $"FAILED: Request {requestId} failed after {MaxAttempts} attempts.";
    }
}

// Input passed from the Durable starter to this orchestrator.
public sealed class TestGenerationOrchestrationInput
{
    public TestGenerationContext GenerationContext { get; set; } = new();

    public string InputContainerName { get; set; } = string.Empty;

    public string InputBlobName { get; set; } = string.Empty;
}

// Input for the AKS validation activity.
public sealed class TestGenerationValidationInput
{
    public string RequestId { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    public string GeneratedResponse { get; set; } = string.Empty;

    public TestGenerationContext GenerationContext { get; set; } = new();

    public string InputContainerName { get; set; } = string.Empty;

    public string InputBlobName { get; set; } = string.Empty;
}

// Output from validation: measured result plus the exact generated test.
public sealed class TestGenerationValidationResult
{
    public ValidationResult ValidationResult { get; set; } = new();

    public string GeneratedTestFileBlobName { get; set; } = string.Empty;

    public string GeneratedTestFilePath { get; set; } = string.Empty;
}

// Input for successful project packaging.
public sealed class TestGenerationPackageInput
{
    public string RequestId { get; set; } = string.Empty;

    public string InputContainerName { get; set; } = string.Empty;

    public string InputBlobName { get; set; } = string.Empty;

    public string GeneratedTestFileBlobName { get; set; } = string.Empty;

    public string GeneratedTestFilePath { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }
}

// Input for the final failure alert.
public sealed class TestGenerationFailureInput
{
    public string RequestId { get; set; } = string.Empty;

    public int Attempts { get; set; }

    public string Diagnostics { get; set; } = string.Empty;
}
