// Purpose: Validates the required Vulcan project layout and supported xUnit test project.

using System.Xml;
using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record LayoutCheckResult(
    IReadOnlyList<string> ErrorCodes,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => ErrorCodes.Count == 0;
}

public static class LayoutChecker
{
    private const string InputDtoPath = "Models/InputDTO.cs";
    private const string OutputDtoPath = "Models/OutputDTO.cs";
    private const string MappingPath = "Transformation/Mapping.cs";
    private const string MappingLogicPath = "Transformation/MappingLogic.md";
    private const string TestProjectPath = "Tests/Tests.csproj";
    private const string PlaceholderPath = "Tests/Transformation/MappingTests.cs";

    public static LayoutCheckResult Check(ProjectZip projectZip)
    {
        ArgumentNullException.ThrowIfNull(projectZip);

        var files = projectZip.Files;
        var layoutProblems = CheckRequiredFiles(files);
        var rootProjects = FindRootProjects(files);

        CheckRootProjectCount(rootProjects, layoutProblems);
        CheckPlaceholderNamespace(files, layoutProblems);

        var testProjectProblems = CheckTestProject(files, rootProjects);

        return CreateResult(layoutProblems, testProjectProblems);
    }

    private static List<string> CheckRequiredFiles(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        var requiredPaths = new[]
        {
            InputDtoPath,
            OutputDtoPath,
            MappingPath,
            MappingLogicPath,
            TestProjectPath,
            PlaceholderPath
        };

        return requiredPaths
            .Where(path => !files.ContainsKey(path))
            .Select(path => $"Required file is missing: {path}")
            .ToList();
    }

    private static List<string> FindRootProjects(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files)
    {
        return files.Keys
            .Where(path =>
                !path.Contains('/') &&
                path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void CheckRootProjectCount(
        List<string> rootProjects,
        List<string> problems)
    {
        if (rootProjects.Count != 1)
        {
            problems.Add(
                $"Expected exactly one root .csproj file; found {rootProjects.Count}.");
        }
    }

    private static void CheckPlaceholderNamespace(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files,
        List<string> problems)
    {
        if (!files.TryGetValue(PlaceholderPath, out var content))
        {
            return;
        }

        var source = System.Text.Encoding.UTF8.GetString(content.Span);
        var syntaxTree = CSharpSyntaxTree.ParseText(source);
        var root = syntaxTree.GetCompilationUnitRoot();

        if (!root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .Any())
        {
            problems.Add(
                $"The placeholder must declare a namespace: {PlaceholderPath}");
        }
    }

    private static List<string> CheckTestProject(
        IReadOnlyDictionary<string, ReadOnlyMemory<byte>> files,
        List<string> rootProjects)
    {
        var problems = new List<string>();

        if (!files.TryGetValue(TestProjectPath, out var testProjectContent))
        {
            return problems;
        }

        var testDocument = TryLoadProject(
            testProjectContent,
            TestProjectPath,
            problems);

        if (testDocument is null)
        {
            return problems;
        }

        CheckRequiredPackages(testDocument, problems);

        if (rootProjects.Count == 1 &&
            files.TryGetValue(rootProjects[0], out var rootContent))
        {
            CheckTargetFrameworks(
                rootContent,
                testDocument,
                rootProjects[0],
                problems);
        }

        return problems;
    }

    private static XDocument? TryLoadProject(
        ReadOnlyMemory<byte> content,
        string path,
        List<string> problems)
    {
        try
        {
            using var stream = new MemoryStream(content.ToArray());
            using var reader = XmlReader.Create(stream, CreateXmlSettings());

            return XDocument.Load(reader);
        }
        catch (XmlException exception)
        {
            problems.Add($"Invalid project XML in {path}: {exception.Message}");
            return null;
        }
    }

    private static XmlReaderSettings CreateXmlSettings()
    {
        return new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
    }

    private static void CheckRequiredPackages(
        XDocument testProject,
        List<string> problems)
    {
        var packageReferences = testProject
            .Descendants("PackageReference")
            .ToList();

        var xunitVersion = FindPackageVersion(packageReferences, "xunit");

        if (!IsXunitVersionTwo(xunitVersion))
        {
            problems.Add(
                "Tests/Tests.csproj must reference xunit with major version 2.");
        }

        if (!packageReferences.Any(reference =>
                IsPackage(reference, "Microsoft.NET.Test.Sdk")))
        {
            problems.Add(
                "Tests/Tests.csproj must reference Microsoft.NET.Test.Sdk.");
        }
    }

    private static string? FindPackageVersion(
        IEnumerable<XElement> packageReferences,
        string packageName)
    {
        var reference = packageReferences.FirstOrDefault(
            element => IsPackage(element, packageName));

        return reference?.Attribute("Version")?.Value
            ?? reference?.Element("Version")?.Value;
    }

    private static bool IsPackage(XElement reference, string packageName)
    {
        var name = reference.Attribute("Include")?.Value
            ?? reference.Attribute("Update")?.Value;

        return string.Equals(
            name,
            packageName,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsXunitVersionTwo(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return false;
        }

        var version = versionText.Split(
            '-', StringSplitOptions.RemoveEmptyEntries)[0];

        return Version.TryParse(version, out var parsedVersion)
            && parsedVersion.Major == 2;
    }

    private static void CheckTargetFrameworks(
        ReadOnlyMemory<byte> rootContent,
        XDocument testProject,
        string rootProjectPath,
        List<string> problems)
    {
        var rootDocument = TryLoadProject(
            rootContent,
            rootProjectPath,
            problems);

        if (rootDocument is null)
        {
            return;
        }

        var rootFramework = GetTargetFramework(rootDocument);
        var testFramework = GetTargetFramework(testProject);

        if (string.IsNullOrWhiteSpace(rootFramework) ||
            string.IsNullOrWhiteSpace(testFramework) ||
            !string.Equals(
                rootFramework,
                testFramework,
                StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                "Tests/Tests.csproj TargetFramework must match the root project TargetFramework.");
        }
    }

    private static string? GetTargetFramework(XDocument project)
    {
        return project
            .Descendants("TargetFramework")
            .Select(element => element.Value.Trim())
            .FirstOrDefault(value => value.Length > 0);
    }

    private static LayoutCheckResult CreateResult(
        List<string> layoutProblems,
        List<string> testProjectProblems)
    {
        var errorCodes = new List<string>();
        var problems = new List<string>();

        if (layoutProblems.Count > 0)
        {
            errorCodes.Add(Jobs.JobErrorCodes.LayoutWrong);
            problems.AddRange(layoutProblems);
        }

        if (testProjectProblems.Count > 0)
        {
            errorCodes.Add(Jobs.JobErrorCodes.TestProjectUnsupported);
            problems.AddRange(testProjectProblems);
        }

        return new LayoutCheckResult(errorCodes, problems);
    }
}