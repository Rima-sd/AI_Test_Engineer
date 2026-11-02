
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Models;

namespace TestEngineerPOC.Services;

public sealed class TargetInventoryService
{
    private readonly ILogger<TargetInventoryService> _logger;

    public TargetInventoryService(
        ILogger<TargetInventoryService> logger)
    {
        _logger = logger;
    }

    // STAGE 8: Inspect the selected source file and create its inventory.
    public TargetInventory CreateInventory(string requestId, string targetFilePath, string targetRelativePath)
    {
        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TargetInventoryStarted] " +
            "Inspecting selected target source file {TargetPath}.",
            requestId,
            targetRelativePath);

        var sourceCode = File.ReadAllText(targetFilePath);

        // Collect namespace declarations.
        var namespaces = Regex
            .Matches(
                sourceCode,
                @"^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)",
                RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        // Collect class, record, and struct names.
        var typeNames = Regex
            .Matches(
                sourceCode,
                @"\b(class|record|struct)\s+([A-Za-z_][A-Za-z0-9_]*)")
            .Select(match => match.Groups[2].Value)
            .Distinct()
            .ToList();

        // Collect using directives as initial dependency clues.
        var usingNamespaces = Regex
            .Matches(
                sourceCode,
                @"^\s*using\s+([A-Za-z_][A-Za-z0-9_.]*)\s*;",
                RegexOptions.Multiline)
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        // Collect likely method declarations.
        var methodNames = Regex
            .Matches(
                sourceCode,
                @"\b(?:public|private|internal|protected)\s+" +
                @"(?:static\s+)?(?:async\s+)?[\w<>,?.\[\]]+\s+" +
                @"([A-Za-z_][A-Za-z0-9_]*)\s*\(")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToList();

        var inventory = new TargetInventory
        {
            RequestId = requestId,
            TargetPath = targetRelativePath,
            SourceLength = sourceCode.Length,
            Namespaces = namespaces,
            TypeNames = typeNames,
            MethodNames = methodNames,
            UsingNamespaces = usingNamespaces
        };

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TargetInventoryCompleted] " +
            "TargetPath={TargetPath}; SourceLength={SourceLength}; " +
            "Namespaces={Namespaces}; Types={Types}; Methods={Methods}; " +
            "UsingNamespaces={UsingNamespaces}",
            requestId,
            inventory.TargetPath,
            inventory.SourceLength,
            string.Join(", ", inventory.Namespaces),
            string.Join(", ", inventory.TypeNames),
            string.Join(", ", inventory.MethodNames),
            string.Join(", ", inventory.UsingNamespaces));

        return inventory;
    }
}
