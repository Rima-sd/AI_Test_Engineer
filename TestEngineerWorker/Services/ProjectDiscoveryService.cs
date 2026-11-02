
/*
 * Purpose:
 * Discovers solution and project files in an extracted .NET workspace.
 * Identifies test projects and their referenced application projects.
 *
 * Cross-platform support:
 * Normalizes Windows and Unix project-reference paths so projects
 * created on Windows can be discovered inside the Linux Worker container.
 */

using System.Xml.Linq;
using TestEngineerWorker.Utils;

namespace TestEngineerWorker.Services;

public sealed class ProjectDiscoveryService : IProjectDiscoveryService
{
    public ProjectInventory Discover(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        var root = Path.GetFullPath(workspaceRoot);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Workspace directory was not found: {root}");
        }

        // Discover solution files, excluding generated folders.
        var solutions = Directory
            .EnumerateFiles(
                root,
                "*" + AppConstant.SolutionExtension,
                SearchOption.AllDirectories)
            .Concat(
                Directory.EnumerateFiles(
                    root,
                    "*" + AppConstant.SolutionXmlExtension,
                    SearchOption.AllDirectories))
            .Where(IsNotGeneratedFile)
            .ToArray();

        // Discover project files, excluding bin and obj.
        var projects = Directory
            .EnumerateFiles(
                root,
                "*" + AppConstant.CSharpProjectExtension,
                SearchOption.AllDirectories)
            .Where(IsNotGeneratedFile)
            .ToArray();

        // Identify test projects.
        var testProjects = projects
            .Where(IsTestProject)
            .ToArray();

        // Identify application projects referenced by test projects.
        var applicationProjects = FindApplicationProjects(
            projects,
            testProjects);

        return new ProjectInventory(
            solutions,
            projects,
            testProjects,
            applicationProjects);
    }

    private static bool IsNotGeneratedFile(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);

        while (!string.IsNullOrWhiteSpace(directory))
        {
            var directoryName = Path.GetFileName(directory);

            if (directoryName.Equals(
                    "bin",
                    StringComparison.OrdinalIgnoreCase) ||
                directoryName.Equals(
                    "obj",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            directory = Path.GetDirectoryName(directory);
        }

        return true;
    }

    private static bool IsTestProject(string projectPath)
    {
        var fileName = Path.GetFileNameWithoutExtension(projectPath);

        if (fileName.Contains(
                "test",
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var document = XDocument.Load(projectPath);

        return document
            .Descendants()
            .Any(element =>
                element.Name.LocalName == "PackageReference" &&
                string.Equals(
                    (string?)element.Attribute("Include"),
                    "Microsoft.NET.Test.Sdk",
                    StringComparison.OrdinalIgnoreCase))
            ||
            document
                .Descendants()
                .Any(element =>
                    element.Name.LocalName == "IsTestProject" &&
                    string.Equals(
                        element.Value,
                        "true",
                        StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> FindApplicationProjects(
        IReadOnlyList<string> projects,
        IReadOnlyList<string> testProjects)
    {
        var discoveredProjects = projects
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var testProjectPaths = testProjects
            .Select(Path.GetFullPath)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var referencedProjects = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var testProject in testProjects)
        {
            var testProjectFullPath = Path.GetFullPath(testProject);

            var document = XDocument.Load(testProjectFullPath);

            var projectReferences = document
                .Descendants()
                .Where(element =>
                    element.Name.LocalName == "ProjectReference");

            foreach (var reference in projectReferences)
            {
                var include = (string?)reference.Attribute("Include");

                if (string.IsNullOrWhiteSpace(include))
                {
                    continue;
                }

                // Normalize Windows paths for Linux container execution.
                var normalizedInclude = include
                    .Trim()
                    .Replace(
                        '\\',
                        Path.DirectorySeparatorChar)
                    .Replace(
                        '/',
                        Path.DirectorySeparatorChar);

                // Resolve the referenced project relative to the test project.
                var referencedPath = Path.GetFullPath(
                    Path.Combine(
                        Path.GetDirectoryName(testProjectFullPath)!,
                        normalizedInclude));

                if (discoveredProjects.Contains(referencedPath) &&
                    !testProjectPaths.Contains(referencedPath))
                {
                    referencedProjects.Add(referencedPath);
                }
            }
        }

        return referencedProjects.ToArray();
    }
}

// Represents the discovered solution and project inventory.
public sealed record ProjectInventory(
    IReadOnlyList<string> SolutionFiles,
    IReadOnlyList<string> ProjectFiles,
    IReadOnlyList<string> TestProjectFiles,
    IReadOnlyList<string> ApplicationProjectFiles);
