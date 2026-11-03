
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Models;

namespace TestEngineerPOC.Services;

public sealed class DependencyInventoryService
{
    private readonly ILogger<DependencyInventoryService> _logger;

    public DependencyInventoryService(
        ILogger<DependencyInventoryService> logger)
    {
        _logger = logger;
    }

    // STAGE 10: Build an inventory for each discovered dependency.
    public IReadOnlyList<DependencyInventory> CreateInventories(
        string requestId,
        string projectRoot,
        IReadOnlyList<string> dependencyPaths)
    {
        var inventories = new List<DependencyInventory>();

        foreach (var relativePath in dependencyPaths)
        {
            var fullPath = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    relativePath.Replace('/', Path.DirectorySeparatorChar)));

            // Ensure the dependency stays inside the extracted project.
            var normalizedRoot = Path.GetFullPath(projectRoot)
                .TrimEnd(Path.DirectorySeparatorChar)
                + Path.DirectorySeparatorChar;

            if (!fullPath.StartsWith(
                    normalizedRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Dependency path escapes project root: {relativePath}");
            }

            if (!File.Exists(fullPath))
            {
                _logger.LogWarning(
                    "[RequestId={RequestId}] " +
                    "[Stage=DependencyInventorySkipped] FileNotFound={FilePath}",
                    requestId,
                    relativePath);

                continue;
            }

            var syntaxTree = CSharpSyntaxTree.ParseText(
                File.ReadAllText(fullPath),
                path: fullPath);

            var root = syntaxTree.GetRoot();

            var inventory = new DependencyInventory
            {
                FilePath = relativePath,

                TypeNames = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .Select(type => type.Identifier.ValueText)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),

                MethodNames = root.DescendantNodes()
                    .OfType<MethodDeclarationSyntax>()
                    .Select(method => method.Identifier.ValueText)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),

                PropertyNames = root.DescendantNodes()
                    .OfType<PropertyDeclarationSyntax>()
                    .Select(property => property.Identifier.ValueText)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),

                ConstructorNames = root.DescendantNodes()
                    .OfType<ConstructorDeclarationSyntax>()
                    .Select(constructor => constructor.Identifier.ValueText)
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList(),

                BaseTypes = root.DescendantNodes()
                    .OfType<BaseTypeDeclarationSyntax>()
                    .OfType<ClassDeclarationSyntax>()
                    .SelectMany(type => type.BaseList?.Types
                        .Select(baseType => baseType.Type.ToString())
                        ?? Enumerable.Empty<string>())
                    .Distinct()
                    .OrderBy(name => name)
                    .ToList()
            };

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DependencyInventoryCompleted] " +
                "FilePath={FilePath}; TypeCount={TypeCount}; " +
                "MethodCount={MethodCount}; PropertyCount={PropertyCount}; " +
                "ConstructorCount={ConstructorCount}",
                requestId,
                inventory.FilePath,
                inventory.TypeNames.Count,
                inventory.MethodNames.Count,
                inventory.PropertyNames.Count,
                inventory.ConstructorNames.Count);

            inventories.Add(inventory);
        }

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=DependencyInventoriesAccepted] " +
            "InventoryCount={InventoryCount}",
            requestId,
            inventories.Count);

        return inventories;
    }
}
