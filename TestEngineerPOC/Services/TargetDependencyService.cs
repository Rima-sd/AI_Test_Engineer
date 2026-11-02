
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.Extensions.Logging;

namespace TestEngineerPOC.Services;

public sealed class TargetDependencyService
{
    private readonly ILogger<TargetDependencyService> _logger;

    public TargetDependencyService(
        ILogger<TargetDependencyService> logger)
    {
        _logger = logger;
    }

    // STAGE 9: Discover source files related to the target using Roslyn.
    public IReadOnlyList<string> DiscoverDependencies(
        string requestId,
        string targetFilePath,
        string projectRoot)
    {
        var rootFullPath = Path.GetFullPath(projectRoot);
        var targetFullPath = Path.GetFullPath(targetFilePath);

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=DependencyDiscoveryStarted] " +
            "Discovering dependencies for {TargetPath}.",
            requestId,
            Path.GetRelativePath(rootFullPath, targetFullPath));

        if (!File.Exists(targetFullPath))
        {
            throw new FileNotFoundException(
                "Target source file was not found.",
                targetFullPath);
        }

        if (!Directory.Exists(rootFullPath))
        {
            throw new DirectoryNotFoundException(
                $"Project root was not found: {rootFullPath}");
        }

        // Read the .NET runtime assemblies available to this Function App.
        // The extracted project has no .csproj, so its package references
        // and project-specific compiler settings are not available here.
        var trustedAssemblies =
            AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;

        if (string.IsNullOrWhiteSpace(trustedAssemblies))
        {
            throw new InvalidOperationException(
                "Could not locate .NET runtime assemblies for Roslyn.");
        }

        var references = trustedAssemblies
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(path => MetadataReference.CreateFromFile(path))
            .ToList();

        // Collect project source files and ignore generated build output.
        var sourceFiles = Directory
            .EnumerateFiles(
                rootFullPath,
                "*.cs",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var fullPath = Path.GetFullPath(path);
                var relativePath = Path.GetRelativePath(
                    rootFullPath, fullPath);

                var segments = relativePath.Split(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar);

                return !segments.Any(segment =>
                    segment.Equals("bin",
                        StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("obj",
                        StringComparison.OrdinalIgnoreCase));
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!sourceFiles.Any(path =>
                string.Equals(
                    Path.GetFullPath(path),
                    targetFullPath,
                    StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException(
                "The target file is not inside the project source tree.");
        }

        // Parse source files into Roslyn syntax trees.
        var syntaxTrees = new List<SyntaxTree>();

        foreach (var sourceFile in sourceFiles)
        {
            try
            {
                syntaxTrees.Add(
                    CSharpSyntaxTree.ParseText(
                        File.ReadAllText(sourceFile),
                        path: Path.GetFullPath(sourceFile)));
            }
            catch (IOException ex)
            {
                _logger.LogWarning(
                    ex,
                    "[RequestId={RequestId}] " +
                    "[Stage=DependencyFileReadSkipped] File={FilePath}",
                    requestId,
                    Path.GetRelativePath(rootFullPath, sourceFile));
            }
        }

        // Locate the target syntax tree.
        var targetTree = syntaxTrees.FirstOrDefault(tree =>
            string.Equals(
                Path.GetFullPath(tree.FilePath),
                targetFullPath,
                StringComparison.OrdinalIgnoreCase));

        if (targetTree is null)
        {
            throw new InvalidOperationException(
                "Roslyn could not parse the target source file.");
        }

        // Create a compilation so Roslyn can resolve symbols and types.
        // Some symbols may remain unresolved without the input project's
        // own NuGet references and compiler settings.
        var compilation = CSharpCompilation.Create(
            assemblyName: "ExtractedProjectAnalysis",
            syntaxTrees: syntaxTrees,
            references: references,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        var semanticModel = compilation.GetSemanticModel(targetTree);
        var targetRoot = targetTree.GetRoot();

        var dependencyPaths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        // Add source files that define a referenced symbol.
        // For methods and properties, also inspect their containing type.
        void AddSymbolSourceLocations(ISymbol? symbol)
        {
            if (symbol is null)
            {
                return;
            }

            if (symbol is IAliasSymbol alias)
            {
                symbol = alias.Target;
            }

            if (symbol is not INamedTypeSymbol &&
                symbol.ContainingType is not null)
            {
                AddSymbolSourceLocations(symbol.ContainingType);
            }

            foreach (var location in symbol.Locations)
            {
                if (!location.IsInSource ||
                    location.SourceTree is null)
                {
                    continue;
                }

                var sourcePath = Path.GetFullPath(
                    location.SourceTree.FilePath);

                // The target itself is not a dependency.
                if (string.Equals(
                    sourcePath,
                    targetFullPath,
                    StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Only accept files inside the extracted project root.
                var relativePath = Path.GetRelativePath(
                    rootFullPath, sourcePath);

                if (relativePath == ".." ||
                    relativePath.StartsWith(
                        ".." + Path.DirectorySeparatorChar,
                        StringComparison.Ordinal) ||
                    Path.IsPathRooted(relativePath))
                {
                    continue;
                }

                dependencyPaths.Add(sourcePath);
            }
        }


        // Inspect actual type references and executable member access.
        foreach (var node in targetRoot.DescendantNodesAndSelf())
        {
            switch (node)
            {
                case IdentifierNameSyntax identifier:
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(identifier);

                        if (symbolInfo.Symbol is ITypeSymbol or IMethodSymbol
                            or IPropertySymbol or IFieldSymbol or IEventSymbol)
                        {
                            AddSymbolSourceLocations(symbolInfo.Symbol);
                        }

                        break;
                    }

                case TypeSyntax typeSyntax:
                    {
                        var typeInfo = semanticModel.GetTypeInfo(typeSyntax);
                        AddSymbolSourceLocations(typeInfo.Type);
                        break;
                    }

                case ObjectCreationExpressionSyntax objectCreation:
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(objectCreation);
                        AddSymbolSourceLocations(symbolInfo.Symbol);
                        break;
                    }

                case InvocationExpressionSyntax invocation:
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(invocation);
                        AddSymbolSourceLocations(symbolInfo.Symbol);
                        break;
                    }

                case MemberAccessExpressionSyntax memberAccess:
                    {
                        var symbolInfo = semanticModel.GetSymbolInfo(memberAccess);
                        AddSymbolSourceLocations(symbolInfo.Symbol);
                        break;
                    }
            }
        }


        // Return distinct project-relative paths in a stable order.
        var result = dependencyPaths
            .Select(path =>
                Path.GetRelativePath(rootFullPath, path)
                    .Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=DependencyDiscoveryCompleted] " +
            "DependencyCount={DependencyCount}; Dependencies={Dependencies}",
            requestId,
            result.Count,
            string.Join(", ", result));

        return result;
    }
}
