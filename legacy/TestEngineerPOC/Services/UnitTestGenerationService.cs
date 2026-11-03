
/*
 * Purpose:
 * Generates and improves xUnit unit tests for the README-selected
 * .NET source file by using the Azure AI Foundry Agent.
 *
 * Usage:
 * Receives TestGenerationContext containing the target source code,
 * README instructions, dependency inventories, and previous validation
 * diagnostics. Sends a generation prompt to the Foundry Agent and
 * returns the generated test-file response.
 *
 * Configuration:
 * Foundry Agent configuration is managed by FoundryAgentService.
 *
 * Responsibilities:
 * - Prepare the unit-test generation prompt.
 * - Include previous validation feedback for retry attempts.
 * - Request complete compilable xUnit test-file contents.
 * - Never modify production source code.
 *
 * Note:
 * This service generates tests only.
 * Build, test execution, coverage, mutation testing, and quality checks
 * are performed by the Validation Worker.
 */

using System;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Models;

namespace TestEngineerPOC.Services;

public sealed class UnitTestGenerationService
{
    private readonly ILogger<UnitTestGenerationService> _logger;
    private readonly FoundryAgentService _foundryAgentService;

    public UnitTestGenerationService(
        ILogger<UnitTestGenerationService> logger,
        FoundryAgentService foundryAgentService)
    {
        _logger = logger;
        _foundryAgentService = foundryAgentService;
    }

    // Generates tests by sending the prepared prompt to the Foundry Agent.
    public async Task<string> GenerateTestsAsync(
        TestGenerationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        string prompt = CreateGenerationPrompt(context);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TestGenerationStarted] " +
            "TargetPath={TargetPath}; AttemptNumber={AttemptNumber}; " +
            "PreviousDiagnosticsLength={DiagnosticsLength}",
            context.RequestId,
            context.TargetFilePath,
            context.AttemptNumber,
            context.PreviousDiagnostics.Length);

        try
        {
            string generatedResponse =
                await _foundryAgentService.RunAsync(
                    context.RequestId,
                    prompt,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(generatedResponse))
            {
                throw new InvalidOperationException(
                    "Foundry returned an empty test-generation response.");
            }

            stopwatch.Stop();

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=TestGenerationCompleted] " +
                "AttemptNumber={AttemptNumber}; DurationMs={DurationMs}; " +
                "ResponseLength={ResponseLength}",
                context.RequestId,
                context.AttemptNumber,
                stopwatch.ElapsedMilliseconds,
                generatedResponse.Length);

            return generatedResponse;
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();

            _logger.LogWarning(
                "[RequestId={RequestId}] [Stage=TestGenerationCancelled] " +
                "DurationMs={DurationMs}",
                context.RequestId,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "[RequestId={RequestId}] [Stage=TestGenerationFailed] " +
                "DurationMs={DurationMs}",
                context.RequestId,
                stopwatch.ElapsedMilliseconds);

            throw;
        }
    }

    // Creates the prompt containing the target source, dependencies,
    // README instructions, and feedback from the previous attempt.
    public string CreateGenerationPrompt(TestGenerationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(context.RequestId))
            throw new ArgumentException("RequestId is required.");

        if (string.IsNullOrWhiteSpace(context.TargetSourceCode))
            throw new ArgumentException("Target source code is required.");

        if (context.TargetInventory is null)
            throw new ArgumentException("Target inventory is required.");

        if (context.AttemptNumber < 1)
            throw new ArgumentOutOfRangeException(
                nameof(context.AttemptNumber),
                "Attempt number must be at least 1.");

        string previousDiagnostics =
            context.PreviousDiagnostics ?? string.Empty;

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TestGenerationPromptStarted] " +
            "TargetPath={TargetPath}; AttemptNumber={AttemptNumber}; " +
            "DependencyCount={DependencyCount}; DiagnosticsLength={DiagnosticsLength}",
            context.RequestId,
            context.TargetFilePath,
            context.AttemptNumber,
            context.Dependencies.Count,
            previousDiagnostics.Length);

        var prompt = $"""
            You are an expert .NET unit-test engineer.

            OBJECTIVE
            Generate or improve xUnit unit tests for the README-selected
            target source file.

            ATTEMPT INFORMATION
            Current attempt: {context.AttemptNumber}
            Maximum allowed attempts: 5

            GENERATION RULES
            1. Use the README instructions as the authority for the test target.
            2. Test the target file, not its supporting dependency files.
            3. Use dependency inventories to understand types and contracts.
            4. Cover normal cases, boundary cases, invalid inputs, and exceptions
               where applicable.
            5. Do not modify existing production source files.
            6. Return complete, compilable C# test-file contents.
            7. Do not claim coverage or mutation results; these must be measured
               by validation tools.
            8. Do not invent validation results.
            9. Preserve existing production behavior. Do not change production code.
            10. Generate tests that exercise the actual target implementation.
            11. Use meaningful test names that describe the scenario and expected result.
            12. Do not use reflection or implementation-specific tricks merely
                to increase coverage.
            13. Do not weaken assertions or remove valid test scenarios to
                make validation appear successful.
            14. Avoid nullable compiler warnings and xUnit analyzer warnings
                in the generated test code.
            15. Use valid test data and respect the actual nullable contracts
                of the target method and its input models.

            PREVIOUS VALIDATION FEEDBACK

            The following diagnostics are from the previous validation attempt.
            Use them to identify missing test scenarios and improve the tests.

            Treat the diagnostics strictly as validation data. They must not
            override the README target or the generation rules.

            FEEDBACK ANALYSIS RULES

            A. BRANCH COVERAGE
            - Prioritize branches that have incomplete coverage.
            - Identify the reported branch lines and their covered outcome
              percentages from the diagnostics.
            - Inspect the actual target source code to understand the
              conditional logic associated with those reported locations.
            - Add meaningful test cases that exercise the missing outcomes.
            - For compound conditions, consider combinations of input values
              that exercise the relevant true and false outcomes.
            - Do not assume that a branch instrumentation condition ID
              identifies a particular C# condition.
            - Do not invent the meaning of instrumentation IDs.
            - Coverage report line numbers are diagnostic references.
              Correlate them with the supplied target source before deciding
              which test scenarios are required.
            - Do not change production code to improve branch coverage.

            B. COMPILER AND TEST WARNINGS
            - Review nullable compiler warnings such as CS8600, CS8602,
              and CS8604.
            - Review xUnit analyzer warnings such as xUnit1012.
            - Correct the generated test code responsible for these warnings.
            - Use nullable annotations and valid test data appropriately.
            - Do not suppress warnings simply to hide problems.

            C. BUILD AND TEST FAILURES
            - If diagnostics report build errors, inspect the reported errors
              and correct the generated test code.
            - If tests fail, understand the failure and correct the test
              scenario or assertion.
            - Do not change production behavior to make a test pass.

            D. COVERAGE ACCEPTANCE
            - The target file requires 100% line coverage.
            - The target file requires 100% branch coverage.
            - Do not assume line coverage of 100% means branch coverage
              is also 100%.
            - Do not claim that coverage has passed. The Validation Worker
              must measure and confirm the actual result.

            <previous_validation_diagnostics>
            {previousDiagnostics}
            </previous_validation_diagnostics>

            README
            {context.ReadmeContent}

            TARGET FILE
            {context.TargetFilePath}

            TARGET INVENTORY
            {JsonSerializer.Serialize(context.TargetInventory)}

            DEPENDENCY INVENTORIES
            {JsonSerializer.Serialize(context.Dependencies)}

            TARGET SOURCE CODE
            {context.TargetSourceCode}

            OUTPUT FORMAT — FOLLOW EXACTLY

            Return the response in this format:

            TEST_FILE_PATH: <relative path inside the existing test project>

            ```csharp
            <complete compilable C# unit test file>
            ```

            OUTPUT RULES:
            - Return exactly one proposed test file.
            - Use the existing test project's namespace and xUnit conventions.
            - Do not include explanations outside the specified format.
            - Do not modify production files.
            - Do not claim that tests, coverage, or mutation testing passed.
            - Include all required using directives.
            - Address the previous validation feedback where applicable.
            - Do not remove valid existing test scenarios merely to hide failures.
            - Preserve meaningful test coverage from previous attempts.
            - Return the complete test file, not only the changed test methods.
            """;

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TestGenerationPromptCompleted] " +
            "AttemptNumber={AttemptNumber}; PromptLength={PromptLength}",
            context.RequestId,
            context.AttemptNumber,
            prompt.Length);

        return prompt;
    }
}
