// Purpose: Exercises Vesta T2 input readers using made-up samples and in-memory ZIP archives.

using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Company.ABC.Vesta.Core.Inputs;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Inspect;

internal static class InputSamplesCheck
{
    private const string VulcanJobId = "price-tiers-code-20261030-01";
    private const string ProjectName = "Company.ABC.PriceTiers";
    private const string MappingId = "price-tiers";
    private const string RowId = "R005";
    private const string RootProjectFile = ProjectName + ".csproj";
    private const string PlaceholderPath = "Tests/Transformation/MappingTests.cs";
    private const string MappingPath = "Transformation/Mapping.cs";
    private const string TestProjectPath = "Tests/Tests.csproj";
    private const string XUnitTwoProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
          </ItemGroup>
        </Project>
        """;
    private const string XUnitThreeProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
          <ItemGroup>
            <PackageReference Include="xunit" Version="3.0.0" />
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
          </ItemGroup>
        </Project>
        """;
    private const string RootProject = """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup>
        </Project>
        """;
    private const string MappingSource = """
        namespace Company.ABC.Models;
        public sealed class InputDTO { public string? Name { get; set; } }
        """;
    private const string MappingTarget = """
        namespace Company.ABC.Models;
        public sealed class OutputDTO { public string? Name { get; set; } }
        """;
    private const string MappingCode = """
        namespace Company.ABC.Transformation;
        public sealed class MappingException : System.Exception { }
        public static class Mapping
        {
            public static Company.ABC.Models.OutputDTO? Map(
                Company.ABC.Models.InputDTO source) => new() { Name = source.Name };
        }
        """;
    private const string Placeholder = """
        namespace Company.ABC.Tests.Transformation;
        public sealed class MappingTests { }
        """;
    private const string MappingLogic = "Copy Name from input to output.";

    private static readonly InputLimits DefaultLimits = new(
        maxZipBytes: 5_000_000,
        maxZipEntries: 100,
        maxZipUnpackedBytes: 5_000_000,
        maxAgentInputBytes: 1_000_000,
        maxTestFileBytes: 262_144);

    public static int Run()
    {
        Console.WriteLine("Vesta T2 input sample checks");
        int failures = 0;

        string sampleRoot = Path.Combine(FindRepositoryRoot(), "samples", "inputs");
        PrepareJsonSamples(sampleRoot);

        failures += CheckVulcanJobs(sampleRoot);
        failures += CheckPackages(sampleRoot);
        failures += CheckTestCases(sampleRoot);
        failures += CheckZipSamples(sampleRoot);
        failures += CheckInputSize(sampleRoot);

        Console.WriteLine();
        Console.WriteLine($"Input sample checks completed: {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    private static int CheckVulcanJobs(string sampleRoot)
    {
        string jobsFolder = Path.Combine(sampleRoot, "vulcan-jobs");
        string donePath = Path.Combine(jobsFolder, "done-compiled.json");
        string notDonePath = Path.Combine(jobsFolder, "not-done.json");
        string notCompiledPath = Path.Combine(jobsFolder, "done-not-compiled.json");
        string uploadPath = Path.Combine(jobsFolder, "upload-origin.json");

        WriteText(donePath, CreateVulcanJob("Done", true, "Minerva"));
        WriteText(notDonePath, CreateVulcanJob("Generating", true, "Minerva"));
        WriteText(notCompiledPath, CreateVulcanJob("Done", false, "Minerva"));
        WriteText(uploadPath, CreateVulcanJob("Done", true, "Upload"));

        int failures = 0;
        failures += CheckJobFile("Vulcan job: Done and compiled", donePath, null);
        failures += CheckJobFile(
            "Vulcan job: not Done",
            notDonePath,
            JobErrorCodes.VulcanJobNotDone);
        failures += CheckJobFile(
            "Vulcan job: Done but not compiled",
            notCompiledPath,
            JobErrorCodes.CodeDoesNotCompile);
        failures += CheckJobFile("Vulcan job: Upload origin", uploadPath, null);
        return failures;
    }

    private static int CheckJobFile(
        string name,
        string path,
        string? expectedCode)
    {
        VulcanJobReadResult result = VulcanJobReader.Read(File.ReadAllBytes(path));
        return Report(name, result.ErrorCode, expectedCode, result.Problems);
    }

    private static int CheckPackages(string sampleRoot)
    {
        string folder = Path.Combine(sampleRoot, "packages");
        string good = CreatePackageJson(MappingJson(), includeAllEntries: true);
        string wrongVersion = CreatePackageJson(MappingJson("9.9"), includeAllEntries: true);
        string twoEntries = CreatePackageJson(MappingJson(), includeAllEntries: false);
        string invalidMapping = CreatePackageJson("{ not-json", includeAllEntries: true);

        string goodPath = WriteText(Path.Combine(folder, "good.json"), good);
        string versionPath = WriteText(Path.Combine(folder, "wrong-contract-version.json"), wrongVersion);
        string twoEntriesPath = WriteText(Path.Combine(folder, "two-entries.json"), twoEntries);
        string invalidMappingPath = WriteText(Path.Combine(folder, "mapping-not-json.json"), invalidMapping);

        int failures = 0;
        failures += CheckPackageFile("Package: good", goodPath, null);
        failures += CheckPackageFile(
            "Package: wrong contractVersion",
            versionPath,
            JobErrorCodes.PackageInvalid);
        failures += CheckPackageFile(
            "Package: two entries",
            twoEntriesPath,
            JobErrorCodes.PackageInvalid);
        failures += CheckPackageFile(
            "Package: mapping is not JSON",
            invalidMappingPath,
            JobErrorCodes.PackageInvalid);
        return failures;
    }

    private static int CheckPackageFile(
        string name,
        string path,
        string? expectedCode)
    {
        PackageReadResult result = PackageReader.Read(File.ReadAllBytes(path));
        return Report(name, result.ErrorCode, expectedCode, result.Problems);
    }

    private static int CheckTestCases(string sampleRoot)
    {
        PackageReadResult package = PackageReader.Read(
            Encoding.UTF8.GetBytes(CreatePackageJson(MappingJson(), includeAllEntries: true)));

        if (package.Mapping is null)
        {
            return Report(
                "Test cases: mapping fixture available",
                JobErrorCodes.PackageInvalid,
                null,
                package.Problems);
        }

        string folder = Path.Combine(sampleRoot, "testcases");
        string goodPath = WriteText(
            Path.Combine(folder, "good.json"),
            """{"mappingId":"price-tiers","mappingVersion":7,"cases":[{"rowId":"R005","targetPath":"Prices[*].Price","input":{"price":10.5},"expected":"10.50"}]}""");
        string unknownPath = WriteText(
            Path.Combine(folder, "unknown-row-id.json"),
            """{"mappingId":"price-tiers","mappingVersion":7,"cases":[{"rowId":"R999","targetPath":"Prices[*].Price","input":{"price":10.5},"expected":"10.50"}]}""");

        int failures = 0;
        failures += CheckTestCaseFile("Test cases: good", goodPath, package.Mapping, null);
        failures += CheckTestCaseFile(
            "Test cases: unknown rowId",
            unknownPath,
            package.Mapping,
            JobErrorCodes.TestCasesInvalid);
        return failures;
    }

    private static int CheckTestCaseFile(
        string name,
        string path,
        ParsedMapping mapping,
        string? expectedCode)
    {
        TestCasesReadResult result = TestCasesReader.Read(
            File.ReadAllBytes(path),
            mapping);
        return Report(name, result.ErrorCode, expectedCode, result.Problems);
    }

    private static int CheckZipSamples(string sampleRoot)
    {
        string sourceRoot = Path.Combine(sampleRoot, "zips-src");
        string baseFolder = Path.Combine(sourceRoot, "valid-project");
        CreateProjectFolder(baseFolder);

        int failures = 0;
        failures += CheckZipFolder(
            "ZIP: good with top folder",
            baseFolder,
            includeTopFolder: true,
            DefaultLimits,
            expectedCode: null);
        failures += CheckZipFolder(
            "ZIP: good without top folder",
            baseFolder,
            includeTopFolder: false,
            DefaultLimits,
            expectedCode: null);

        string missingPlaceholder = Path.Combine(sourceRoot, "placeholder-missing");
        CopyProjectFolder(baseFolder, missingPlaceholder);
        File.Delete(Path.Combine(missingPlaceholder, PlaceholderPath));
        failures += CheckZipFolder(
            "ZIP layout: placeholder missing",
            missingPlaceholder,
            includeTopFolder: true,
            DefaultLimits,
            JobErrorCodes.LayoutWrong);

        string noNamespace = Path.Combine(sourceRoot, "placeholder-no-namespace");
        CopyProjectFolder(baseFolder, noNamespace);
        WriteText(Path.Combine(noNamespace, PlaceholderPath), "public sealed class MappingTests { }");
        failures += CheckZipFolder(
            "ZIP layout: placeholder without namespace",
            noNamespace,
            includeTopFolder: true,
            DefaultLimits,
            JobErrorCodes.LayoutWrong);

        string xunitThree = Path.Combine(sourceRoot, "xunit-three");
        CopyProjectFolder(baseFolder, xunitThree);
        WriteText(Path.Combine(xunitThree, TestProjectPath), XUnitThreeProject);
        failures += CheckZipFolder(
            "ZIP layout: xUnit 3",
            xunitThree,
            includeTopFolder: true,
            DefaultLimits,
            JobErrorCodes.TestProjectUnsupported);

        string twoRootProjects = Path.Combine(sourceRoot, "two-root-projects");
        CopyProjectFolder(baseFolder, twoRootProjects);
        WriteText(Path.Combine(twoRootProjects, "Extra.csproj"), RootProject);
        failures += CheckZipFolder(
            "ZIP layout: two root csproj files",
            twoRootProjects,
            includeTopFolder: true,
            DefaultLimits,
            JobErrorCodes.LayoutWrong);

        failures += CheckUnsafeZip(
            "ZIP safety: ../ entry",
            baseFolder,
            archive => archive.CreateEntry("../escape.txt"),
            JobErrorCodes.ZipUnsafe);
        failures += CheckUnsafeZip(
            "ZIP safety: duplicate names ignoring case",
            baseFolder,
            archive => archive.CreateEntry($"{ProjectName}/README.MD"),
            JobErrorCodes.ZipUnsafe);

        string compileErrors = Path.Combine(sourceRoot, "compile-errors");
        CopyProjectFolder(baseFolder, compileErrors);
        WriteText(Path.Combine(compileErrors, "COMPILE-ERRORS.md"), "Compiler errors were reported.");
        failures += CheckZipFolder(
            "ZIP: COMPILE-ERRORS.md",
            compileErrors,
            includeTopFolder: true,
            DefaultLimits,
            JobErrorCodes.CodeDoesNotCompile);

        InputLimits tinyZipLimit = new(
            maxZipBytes: 1,
            maxZipEntries: 100,
            maxZipUnpackedBytes: 5_000_000,
            maxAgentInputBytes: 1_000_000,
            maxTestFileBytes: 262_144);
        failures += CheckZipFolder(
            "ZIP safety: archive exceeds MaxZipBytes",
            baseFolder,
            includeTopFolder: true,
            tinyZipLimit,
            JobErrorCodes.ZipUnsafe);

        InputLimits tinyUnpackedLimit = new(
            maxZipBytes: 5_000_000,
            maxZipEntries: 100,
            maxZipUnpackedBytes: 1,
            maxAgentInputBytes: 1_000_000,
            maxTestFileBytes: 262_144);
        failures += CheckZipFolder(
            "ZIP safety: unpacked bytes exceed MaxZipUnpackedBytes",
            baseFolder,
            includeTopFolder: true,
            tinyUnpackedLimit,
            JobErrorCodes.ZipUnsafe);

        InputLimits tinyEntryLimit = new(
            maxZipBytes: 5_000_000,
            maxZipEntries: 2,
            maxZipUnpackedBytes: 5_000_000,
            maxAgentInputBytes: 1_000_000,
            maxTestFileBytes: 262_144);
        failures += CheckZipFolder(
            "ZIP safety: entry count exceeds MaxZipEntries",
            baseFolder,
            includeTopFolder: true,
            tinyEntryLimit,
            JobErrorCodes.ZipUnsafe);

        return failures;
    }

    private static int CheckZipFolder(
        string name,
        string folder,
        bool includeTopFolder,
        InputLimits limits,
        string? expectedCode)
    {
        byte[] archiveBytes = BuildZip(folder, includeTopFolder);
        ProjectZipReadResult zipResult = ProjectZipReader.Read(archiveBytes, limits);

        if (zipResult.ErrorCode is not null)
        {
            return Report(name, zipResult.ErrorCode, expectedCode, zipResult.Problems);
        }

        if (zipResult.Zip is null)
        {
            return Report(name, "NoProjectZip", expectedCode, ["Reader returned no ZIP."]);
        }

        LayoutCheckResult layout = LayoutChecker.Check(zipResult.Zip);
        string? actualCode = layout.ErrorCodes.Count == 0
            ? null
            : layout.ErrorCodes[0];
        return Report(name, actualCode, expectedCode, layout.Problems);
    }

    private static int CheckUnsafeZip(
        string name,
        string folder,
        Action<ZipArchive> addUnsafeEntry,
        string expectedCode)
    {
        byte[] archiveBytes = BuildZip(folder, includeTopFolder: true, addUnsafeEntry);
        ProjectZipReadResult result = ProjectZipReader.Read(archiveBytes, DefaultLimits);
        return Report(name, result.ErrorCode, expectedCode, result.Problems);
    }

    private static int CheckInputSize(string sampleRoot)
    {
        ProjectZipReadResult zip = ProjectZipReader.Read(
            BuildZip(Path.Combine(sampleRoot, "zips-src", "valid-project"), includeTopFolder: true),
            DefaultLimits);

        if (zip.Zip is null)
        {
            return Report(
                "Input size: valid ZIP fixture available",
                zip.ErrorCode,
                null,
                zip.Problems);
        }

        byte[] packageBytes = Encoding.UTF8.GetBytes(
            CreatePackageJson(MappingJson(), includeAllEntries: true));
        InputLimits tinyAgentLimit = new(
            maxZipBytes: DefaultLimits.MaxZipBytes,
            maxZipEntries: DefaultLimits.MaxZipEntries,
            maxZipUnpackedBytes: DefaultLimits.MaxZipUnpackedBytes,
            maxAgentInputBytes: 1,
            maxTestFileBytes: DefaultLimits.MaxTestFileBytes);
        InputSizeCheckResult result = InputSizeChecker.Check(
            packageBytes,
            zip.Zip,
            tinyAgentLimit);

        return Report(
            "Input size: package plus four generated files",
            result.ErrorCode,
            JobErrorCodes.InputTooLarge,
            result.Problems);
    }

    private static void PrepareJsonSamples(string sampleRoot)
    {
        Directory.CreateDirectory(Path.Combine(sampleRoot, "vulcan-jobs"));
        Directory.CreateDirectory(Path.Combine(sampleRoot, "packages"));
        Directory.CreateDirectory(Path.Combine(sampleRoot, "testcases"));
        Directory.CreateDirectory(Path.Combine(sampleRoot, "zips-src"));
    }

    private static string CreateVulcanJob(
        string state,
        bool compiled,
        string originKind)
    {
        string? runId = originKind == "Minerva" ? "price-tiers-20261030-01" : null;
        int? packageVersion = originKind == "Minerva" ? 7 : null;

        return JsonSerializer.Serialize(new
        {
            jobId = VulcanJobId,
            mappingId = MappingId,
            origin = new { kind = originKind, runId, packageVersion },
            project = new { fullName = ProjectName },
            state,
            generation = 2,
            output = new
            {
                zipPath = $"{VulcanJobId}/{ProjectName}.zip",
                compiled,
                generation = 2
            }
        });
    }

    private static string CreatePackageJson(
        string mappingContent,
        bool includeAllEntries)
    {
        var files = new List<object>
        {
            new
            {
                FileName = "source.json",
                ContentType = "application/json",
                Content = "{}",
                Remarks = "source schema"
            },
            new
            {
                FileName = "target.json",
                ContentType = "application/json",
                Content = "{}",
                Remarks = "target schema"
            },
            new
            {
                FileName = "mapping.json",
                ContentType = "application/json",
                Content = mappingContent,
                Remarks = "approved mapping"
            }
        };

        if (!includeAllEntries)
        {
            files.RemoveAt(2);
        }

        return JsonSerializer.Serialize(new { Files = files });
    }

    private static string MappingJson(string contractVersion = "0.1")
    {
        return JsonSerializer.Serialize(new
        {
            contractVersion,
            mappingId = MappingId,
            scopes = new[] { new { scopeId = "S001", name = "Prices" } },
            rows = new[]
            {
                new
                {
                    rowId = RowId,
                    scopeId = "S001",
                    target = new { path = "Prices[*].Price" },
                    rule = new { type = "Copy" }
                }
            }
        });
    }

    private static void CreateProjectFolder(string folder)
    {
        WriteText(Path.Combine(folder, RootProjectFile), RootProject);
        WriteText(Path.Combine(folder, "Models", "InputDTO.cs"), MappingSource);
        WriteText(Path.Combine(folder, "Models", "OutputDTO.cs"), MappingTarget);
        WriteText(Path.Combine(folder, MappingPath), MappingCode);
        WriteText(Path.Combine(folder, "Transformation", "MappingLogic.md"), MappingLogic);
        WriteText(Path.Combine(folder, TestProjectPath), XUnitTwoProject);
        WriteText(Path.Combine(folder, PlaceholderPath), Placeholder);
        WriteText(Path.Combine(folder, "README.md"), "Synthetic Inspect fixture.");
    }

    private static void CopyProjectFolder(string source, string destination)
    {
        if (Directory.Exists(destination))
        {
            Directory.Delete(destination, recursive: true);
        }

        foreach (string directory in Directory.EnumerateDirectories(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(
                Path.Combine(destination, Path.GetRelativePath(source, directory)));
        }

        foreach (string file in Directory.EnumerateFiles(
                     source,
                     "*",
                     SearchOption.AllDirectories))
        {
            string target = Path.Combine(
                destination,
                Path.GetRelativePath(source, file));
            string? targetDirectory = Path.GetDirectoryName(target);
            if (targetDirectory is not null)
            {
                Directory.CreateDirectory(targetDirectory);
            }

            File.Copy(file, target, overwrite: true);
        }
    }

    private static byte[] BuildZip(
        string folder,
        bool includeTopFolder,
        Action<ZipArchive>? addExtraEntry = null)
    {
        using MemoryStream output = new();

        using (ZipArchive archive = new(
                   output,
                   ZipArchiveMode.Create,
                   leaveOpen: true))
        {
            foreach (string file in Directory.EnumerateFiles(
                         folder,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(folder, file)
                    .Replace(Path.DirectorySeparatorChar, '/');
                string entryName = includeTopFolder
                    ? $"{ProjectName}/{relative}"
                    : relative;
                ZipArchiveEntry entry = archive.CreateEntry(entryName);
                using Stream entryStream = entry.Open();
                byte[] content = File.ReadAllBytes(file);
                entryStream.Write(content);
            }

            addExtraEntry?.Invoke(archive);
        }

        return output.ToArray();
    }

    private static string WriteText(string path, string content)
    {
        string? directory = Path.GetDirectoryName(path);
        if (directory is not null)
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return path;
    }

    private static int Report(
        string name,
        string? actualCode,
        string? expectedCode,
        IReadOnlyList<string> problems)
    {
        bool passed = string.Equals(
            actualCode,
            expectedCode,
            StringComparison.Ordinal);

        Console.WriteLine(
            $"{(passed ? "PASS" : "FAIL")}: {name} (expected: {expectedCode ?? "success"}, actual: {actualCode ?? "success"})");

        if (!passed || problems.Count > 0)
        {
            foreach (string problem in problems)
            {
                Console.WriteLine($"  - {problem}");
            }
        }

        return passed ? 0 : 1;
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(Environment.CurrentDirectory);

        while (current is not null)
        {
            string coreProject = Path.Combine(
                current.FullName,
                "src",
                "Company.ABC.Vesta.Core",
                "Company.ABC.Vesta.Core.csproj");

            if (File.Exists(coreProject))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the Vesta repository root from the current directory.");
    }
}
