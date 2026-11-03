
/*
 * Purpose: Builds and validates the initial Vesta agent request from the
 * validated Vulcan, Minerva and project ZIP inputs.
 * The request follows the embedded agent-request JSON schema.
 */

using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Json.Schema;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Agents;

public static class TestRequestBuilder
{
    private const string AgentName = "vesta";
    private const string AgentContractVersion = "0.1";
    private const string MappingContractVersion = "0.1";

    private const string TestFilePath =
        "Tests/Transformation/MappingTests.cs";

    private const string TestClassName = "MappingTests";

    private static readonly string[] RequiredGeneratedFilePaths =
    [
        "Models/InputDTO.cs",
        "Models/OutputDTO.cs",
        "Transformation/Mapping.cs",
        "Transformation/MappingLogic.md"
    ];

    private static readonly JsonSchema RequestSchema = LoadRequestSchema();

    private static readonly EvaluationOptions SchemaEvaluationOptions = new()
    {
        OutputFormat = OutputFormat.Hierarchical,
        RequireFormatValidation = true
    };

    public static AgentRequest Build(
        string vestaJobId,
        VulcanJob vulcanJob,
        PackageReadResult package,
        ProjectZip projectZip,
        TestCasesReadResult testCases)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vestaJobId);
        ArgumentNullException.ThrowIfNull(vulcanJob);
        ArgumentNullException.ThrowIfNull(package);
        ArgumentNullException.ThrowIfNull(projectZip);
        ArgumentNullException.ThrowIfNull(testCases);

        ValidateInputs(vestaJobId, vulcanJob, package, testCases);

        string testNamespace = ReadPlaceholderNamespace(projectZip);

        List<AgentGeneratedFile> generatedFiles = BuildGeneratedFiles(projectZip);

        IReadOnlyList<AgentMappingRow> rows =
            RowListBuilder.Build(package.Mapping!)
                .Select(row => new AgentMappingRow
                {
                    RowId = row.RowId,
                    ScopeId = row.ScopeId,
                    TargetPath = row.TargetPath,
                    RuleType = row.RuleType
                })
                .ToList();

        IReadOnlyList<AgentTestCase> agentTestCases =
            testCases.Cases
                .Select(testCase => new AgentTestCase
                {
                    RowId = testCase.RowId,
                    TargetPath = testCase.TargetPath,
                    Input = testCase.Input,
                    Expected = testCase.Expected
                })
                .ToList();

        AgentRequest request = new()
        {
            Agent = AgentName,
            RequestId = Guid.NewGuid().ToString(),
            JobId = vestaJobId,

            ContractVersions = new AgentContractVersions
            {
                Agent = AgentContractVersion,
                Mapping = MappingContractVersion
            },

            Input = new AgentRequestInput
            {
                Mode = "Initial",

                TargetTestFile = new AgentTestFile
                {
                    Path = TestFilePath,
                    Namespace = testNamespace,
                    ClassName = TestClassName
                },

                Package = new AgentPackage
                {
                    Files = package.Files
                        .Select(file => file.FileName)
                        .ToList()
                },

                GeneratedFiles = generatedFiles,
                Rows = rows,
                TestCases = agentTestCases,

                PreviousTestFile = null,
                Problems = Array.Empty<string>()
            }
        };

        ValidateRequestSchema(request);

        return request;
    }

    private static void ValidateInputs(
        string vestaJobId,
        VulcanJob vulcanJob,
        PackageReadResult package,
        TestCasesReadResult testCases)
    {
        if (!JobIds.IsValidVestaJobId(vestaJobId))
        {
            throw new ArgumentException(
                "The Vesta job ID is invalid.",
                nameof(vestaJobId));
        }

        if (!string.Equals(
                vulcanJob.State,
                "Done",
                StringComparison.Ordinal) ||
            !vulcanJob.OutputCompiled)
        {
            throw new ArgumentException(
                "The Vulcan job must be Done and its output must be compiled.",
                nameof(vulcanJob));
        }

        if (!package.IsValid || package.Mapping is null)
        {
            throw new ArgumentException(
                "The Minerva package must be valid and contain a parsed mapping.",
                nameof(package));
        }

        if (package.Files.Count != 3 ||
            package.SourceFile is null ||
            package.TargetFile is null ||
            package.MappingFile is null)
        {
            throw new ArgumentException(
                "The Minerva package must contain exactly one source, target and mapping file.",
                nameof(package));
        }

        if (!string.Equals(
                package.Mapping.MappingId,
                vulcanJob.MappingId,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "The mapping ID in the package does not match the Vulcan job.",
                nameof(package));
        }

        if (!testCases.IsValid)
        {
            throw new ArgumentException(
                "The supplied Minerva test cases are invalid.",
                nameof(testCases));
        }
    }

    private static List<AgentGeneratedFile> BuildGeneratedFiles(ProjectZip projectZip)
    {
        List<AgentGeneratedFile> generatedFiles = [];

        foreach (string path in RequiredGeneratedFilePaths)
        {
            if (!projectZip.Files.TryGetValue(path, out ReadOnlyMemory<byte> content))
            {
                throw new ArgumentException(
                    $"Required generated project file is missing: {path}",
                    nameof(projectZip));
            }

            generatedFiles.Add(new AgentGeneratedFile
            {
                Path = path,
                Content = Encoding.UTF8.GetString(content.Span)
            });
        }

        if (generatedFiles.Count != 4 ||
            generatedFiles.Select(file => file.Path)
                .Distinct(StringComparer.Ordinal)
                .Count() != 4)
        {
            throw new InvalidOperationException(
                "The agent request must contain each of the four required generated files exactly once.");
        }

        return generatedFiles;
    }

    private static string ReadPlaceholderNamespace(ProjectZip projectZip)
    {
        if (!projectZip.Files.TryGetValue(
                TestFilePath,
                out ReadOnlyMemory<byte> placeholderContent))
        {
            throw new ArgumentException(
                $"The test placeholder is missing: {TestFilePath}",
                nameof(projectZip));
        }

        string source = Encoding.UTF8.GetString(
            placeholderContent.Span);

        SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(source);
        CompilationUnitSyntax root = syntaxTree.GetCompilationUnitRoot();

        BaseNamespaceDeclarationSyntax? namespaceDeclaration =
            root.DescendantNodes()
                .OfType<BaseNamespaceDeclarationSyntax>()
                .FirstOrDefault();

        if (namespaceDeclaration is null)
        {
            throw new ArgumentException(
                $"The test placeholder must declare a namespace: {TestFilePath}",
                nameof(projectZip));
        }

        string namespaceName = namespaceDeclaration.Name.ToString();

        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            throw new ArgumentException(
                $"The test placeholder namespace is empty: {TestFilePath}",
                nameof(projectZip));
        }

        return namespaceName;
    }

    private static void ValidateRequestSchema(AgentRequest request)
    {
        JsonElement requestDocument = JsonSerializer.SerializeToElement(
            request,
            ContractJson.Options);

        EvaluationResults results = RequestSchema.Evaluate(
            requestDocument,
            SchemaEvaluationOptions);

        if (results.IsValid)
        {
            return;
        }

        List<string> problems = [];

        CollectSchemaProblems(results, problems);

        throw new ContractViolationException(
            ContractKind.AgentRequest,
            problems);
    }

    private static JsonSchema LoadRequestSchema()
    {
        Assembly assembly = typeof(TestRequestBuilder).Assembly;

        const string resourceName =
            "Schemas.agent-request.schema.json";

        using Stream stream =
            assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException(
                $"Embedded schema resource not found: {resourceName}");

        using JsonDocument schemaDocument = JsonDocument.Parse(stream);

        return JsonSchema.Build(
            schemaDocument.RootElement.Clone(),
            new BuildOptions
            {
                Dialect = Dialect.Draft202012
            });
    }

    private static void CollectSchemaProblems(
        EvaluationResults result,
        ICollection<string> problems)
    {
        if (result.IsValid)
        {
            return;
        }

        if (result.Errors is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> error in result.Errors)
            {
                problems.Add(
                    $"{result.InstanceLocation}: {error.Key}: {error.Value}");
            }
        }

        if (result.Details is null)
        {
            return;
        }

        foreach (EvaluationResults detail in result.Details)
        {
            CollectSchemaProblems(detail, problems);
        }
    }
}
