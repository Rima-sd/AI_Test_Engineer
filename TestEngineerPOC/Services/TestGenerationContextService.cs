using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Models;

namespace TestEngineerPOC.Services;

public sealed class TestGenerationContextService
{
    private readonly ILogger<TestGenerationContextService> _logger;

    public TestGenerationContextService(
        ILogger<TestGenerationContextService> logger)
    {
        _logger = logger;
    }

    // STAGE 11: Prepare structured context for unit-test generation.
    public TestGenerationContext CreateContext(
        string requestId,
        string targetFilePath,
        string targetRelativePath,
        string readmeContent,
        TargetInventory targetInventory,
        IReadOnlyList<DependencyInventory> dependencyInventories,
        int attemptNumber = 1,
        string previousDiagnostics = "")
    {
        if (attemptNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(attemptNumber),
                "Attempt number must be at least 1.");
        }

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TestContextCreationStarted] " +
            "TargetPath={TargetPath}; AttemptNumber={AttemptNumber}; " +
            "DependencyCount={DependencyCount}",
            requestId,
            targetRelativePath,
            attemptNumber,
            dependencyInventories.Count);

        if (!File.Exists(targetFilePath))
        {
            throw new FileNotFoundException(
                "Target source file was not found.",
                targetFilePath);
        }

        var context = new TestGenerationContext
        {
            RequestId = requestId,
            TargetFilePath = targetRelativePath,
            TargetSourceCode = File.ReadAllText(targetFilePath),
            ReadmeContent = readmeContent,
            TargetInventory = targetInventory,
            Dependencies = new List<DependencyInventory>(
                dependencyInventories),

            // Ralph loop feedback
            AttemptNumber = attemptNumber,
            PreviousDiagnostics = previousDiagnostics ?? string.Empty
        };

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TestContextCreationCompleted] " +
            "TargetPath={TargetPath}; AttemptNumber={AttemptNumber}; " +
            "SourceLength={SourceLength}; DependencyCount={DependencyCount}; " +
            "ReadmeLength={ReadmeLength}; PreviousDiagnosticsLength={DiagnosticsLength}",
            requestId,
            context.TargetFilePath,
            context.AttemptNumber,
            context.TargetSourceCode.Length,
            context.Dependencies.Count,
            context.ReadmeContent.Length,
            context.PreviousDiagnostics.Length);

        return context;
    }
}