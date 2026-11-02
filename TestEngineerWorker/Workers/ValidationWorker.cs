
/*
 * Purpose:
 * Coordinates one validation attempt for the AI Test Engineer POC.
 *
 * Responsibilities:
 * - Validate the incoming request.
 * - Download the input project ZIP from Blob Storage.
 * - Extract the project into an isolated workspace.
 * - Discover application and test projects.
 * - Build application projects.
 * - Execute unit tests.
 * - Measure line and branch coverage for the README-selected target file.
 * - Return structured validation diagnostics.
 *
 * Note:
 * Mutation testing and quality checks will be implemented in subsequent steps.
 * The Azure Function App, not this worker, controls the five-attempt Ralph loop.
 */

using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Options;
using TestEngineer.Contracts;
using TestEngineerWorker.Options;
using TestEngineerWorker.Services;
using TestEngineerWorker.Utils;

namespace TestEngineerWorker.Workers;

public sealed class ValidationWorker
{
    private readonly IBlobProjectDownloader _blobProjectDownloader;
    private readonly IWorkspaceService _workspaceService;
    private readonly IProjectDiscoveryService _projectDiscoveryService;
    private readonly ICodeCoverageService _codeCoverageService;
    private readonly BlobStorageOptions _blobStorageOptions;

    private static readonly TimeSpan CommandTimeout =
        TimeSpan.FromMinutes(10);

    public ValidationWorker(
        IBlobProjectDownloader blobProjectDownloader,
        IWorkspaceService workspaceService,
        IProjectDiscoveryService projectDiscoveryService,
        ICodeCoverageService codeCoverageService,
        IOptions<BlobStorageOptions> blobStorageOptions)
    {
        _blobProjectDownloader = blobProjectDownloader
            ?? throw new ArgumentNullException(nameof(blobProjectDownloader));

        _workspaceService = workspaceService
            ?? throw new ArgumentNullException(nameof(workspaceService));

        _projectDiscoveryService = projectDiscoveryService
            ?? throw new ArgumentNullException(nameof(projectDiscoveryService));

        _codeCoverageService = codeCoverageService
            ?? throw new ArgumentNullException(nameof(codeCoverageService));

        _blobStorageOptions = blobStorageOptions?.Value
            ?? throw new ArgumentNullException(nameof(blobStorageOptions));
    }

    public async Task<ValidationResult> ValidateAsync(
        ValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            throw new ArgumentException(
                "RequestId is required.",
                nameof(request));
        }

        if (request.AttemptNumber is < 1 or > AppConstant.MaximumValidationAttempts)
        {
            throw new ArgumentOutOfRangeException(
                nameof(request),
                $"AttemptNumber must be between 1 and {AppConstant.MaximumValidationAttempts}.");
        }

        if (string.IsNullOrWhiteSpace(request.InputBlobName))
        {
            throw new ArgumentException(
                "InputBlobName is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.TargetFilePath))
        {
            throw new ArgumentException(
                "TargetFilePath is required. It must come from the README-based test-generation process.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.GeneratedTestFilePath))
        {
            throw new ArgumentException(
                "GeneratedTestFilePath is required.",
                nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.GeneratedTestFileBlobName))
        {
            throw new ArgumentException(
                "GeneratedTestFileBlobName is required.",
                nameof(request));
        }

        var containerName = _blobStorageOptions.ContainerName;

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                "Blob Storage container name is not configured.");
        }

        // Do not use RequestId as a filesystem path.
        // Generate a unique workspace for every validation attempt.
        var attemptRoot = Path.Combine(
            Path.GetTempPath(),
            AppConstant.WorkerWorkspaceFolder,
            Guid.NewGuid().ToString("N"));

        var zipPath = Path.Combine(
            attemptRoot,
            AppConstant.InputZipFileName);

        var workspaceRoot = Path.Combine(
            attemptRoot,
            AppConstant.ExtractedWorkspaceFolder);

        string? preservedCoverageDirectory = null;

        try
        {
            Directory.CreateDirectory(attemptRoot);

            var diagnostics = new StringBuilder();

            // Step 1: Download the input project ZIP.
            ConsoleProgress.Show(
                "Step 1/7: Downloading project ZIP from Blob Storage...");

            await _blobProjectDownloader.DownloadAsync(
                containerName,
                request.InputBlobName,
                zipPath,
                cancellationToken);

            diagnostics.AppendLine("ZIP download completed.");
            ConsoleProgress.ShowSuccess("Project ZIP downloaded.");

            // Step 2: Extract the ZIP safely.
            ConsoleProgress.Show("Step 2/7: Extracting project ZIP...");

            Directory.CreateDirectory(workspaceRoot);

            _workspaceService.ExtractZipSafely(
                zipPath,
                workspaceRoot);

            diagnostics.AppendLine("ZIP extraction completed.");
            ConsoleProgress.ShowSuccess("Project extracted successfully.");

            // Step 3: Discover solutions and projects.
            ConsoleProgress.Show(
                "Step 3/7: Discovering projects and dependencies...");

            var inventory = _projectDiscoveryService.Discover(
                workspaceRoot);
            // Step 4: Install the generated test file into the test project.
            ConsoleProgress.Show(
                "Step 4/7: Installing Foundry-generated test file...");

            if (inventory.TestProjectFiles.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one test project, but found " +
                    $"{inventory.TestProjectFiles.Count}.");
            }

            var testProjectFile = inventory.TestProjectFiles[0];

            var testProjectRoot = Path.GetDirectoryName(
                Path.GetFullPath(testProjectFile))!;

            var generatedTestDestination = GetSafeGeneratedTestPath(
                testProjectRoot,
                request.GeneratedTestFilePath);

            var generatedTestTemporaryPath = Path.Combine(
                attemptRoot,
                "generated-test-download.cs");

            if (File.Exists(generatedTestDestination))
            {
                throw new IOException(
                    $"Generated test destination already exists. " +
                    $"Existing files will not be overwritten: {generatedTestDestination}");
            }

            await _blobProjectDownloader.DownloadAsync(
                containerName,
                request.GeneratedTestFileBlobName,
                generatedTestTemporaryPath,
                cancellationToken);

            var generatedTestDirectory = Path.GetDirectoryName(
                generatedTestDestination)!;

            Directory.CreateDirectory(generatedTestDirectory);

            // File.Move without overwrite protects existing files.
            File.Move(
                generatedTestTemporaryPath,
                generatedTestDestination);

            diagnostics.AppendLine(
                $"Generated test file installed: {generatedTestDestination}");

            ConsoleProgress.ShowSuccess(
                "Generated test file installed successfully.");

            diagnostics.AppendLine(
                $"Solutions discovered: {inventory.SolutionFiles.Count}");

            diagnostics.AppendLine(
                $"Projects discovered: {inventory.ProjectFiles.Count}");

            diagnostics.AppendLine(
                $"Application projects discovered: {inventory.ApplicationProjectFiles.Count}");

            diagnostics.AppendLine(
                $"Test projects discovered: {inventory.TestProjectFiles.Count}");

            diagnostics.AppendLine(
                $"README-selected target file: {request.TargetFilePath}");

            if (inventory.ApplicationProjectFiles.Count == 0)
            {
                diagnostics.AppendLine(
                    "No application projects referenced by test projects were discovered.");

                return CreateResult(
                    request,
                    buildPassed: false,
                    testsPassed: false,
                    diagnostics.ToString());
            }

            // Step 5: Build application projects.
            ConsoleProgress.Show(
                "Step 5/7: Building application projects...");

            var buildPassed = true;

            foreach (var applicationProject in inventory.ApplicationProjectFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                diagnostics.AppendLine();
                diagnostics.AppendLine(
                    $"Building application project: {applicationProject}");

                var buildResult = await RunDotnetCommandAsync(
                    workspaceRoot,
                    new[]
                    {
                        "build",
                        applicationProject,
                        "--configuration", "Release",
                        "--verbosity", "minimal",
                        "--nologo"
                    },
                    cancellationToken);

                diagnostics.AppendLine(buildResult.Output);

                if (buildResult.TimedOut)
                {
                    diagnostics.AppendLine(
                        "Application build timed out.");

                    buildPassed = false;
                    break;
                }

                if (buildResult.ExitCode != 0)
                {
                    diagnostics.AppendLine(
                        $"Application build failed. Exit code: {buildResult.ExitCode}");

                    buildPassed = false;
                    break;
                }

                diagnostics.AppendLine(
                    "Application build succeeded.");
            }

            // Do not execute tests if the application build failed.
            if (!buildPassed)
            {
                return CreateResult(
                    request,
                    buildPassed: false,
                    testsPassed: false,
                    diagnostics.ToString());
            }

            // Step 6: Execute test projects.
            ConsoleProgress.Show(
                "Step 6/7: Running unit tests and collecting coverage...");

            var testsPassed = true;

            foreach (var testProject in inventory.TestProjectFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();

                diagnostics.AppendLine();
                diagnostics.AppendLine(
                    $"Executing test project: {testProject}");

                var testResult = await RunDotnetCommandAsync(
                    workspaceRoot,
                    new[]
                    {
                        "test",
                        testProject,
                        "--configuration", "Release",
                        "--collect:XPlat Code Coverage",
                        "--verbosity", "minimal",
                        "--nologo"
                    },
                    cancellationToken);

                diagnostics.AppendLine(testResult.Output);

                if (testResult.TimedOut)
                {
                    diagnostics.AppendLine(
                        "Test execution timed out.");

                    testsPassed = false;
                    break;
                }

                if (testResult.ExitCode != 0)
                {
                    diagnostics.AppendLine(
                        $"Test execution failed. Exit code: {testResult.ExitCode}");

                    testsPassed = false;
                    break;
                }

                diagnostics.AppendLine(
                    "Test execution succeeded.");
            }

            // Preserve Cobertura reports before the temporary workspace is deleted.
            var coverageReportFiles = Directory
                .EnumerateFiles(
                    workspaceRoot,
                    "coverage.cobertura.xml",
                    SearchOption.AllDirectories)
                .ToArray();

            if (coverageReportFiles.Length > 0)
            {
                preservedCoverageDirectory = Path.Combine(
                    Path.GetTempPath(),
                    AppConstant.WorkerWorkspaceFolder,
                    "CoverageReports",
                    Guid.NewGuid().ToString("N"));

                Directory.CreateDirectory(preservedCoverageDirectory);

                for (var i = 0; i < coverageReportFiles.Length; i++)
                {
                    var destination = Path.Combine(
                        preservedCoverageDirectory,
                        $"coverage-report-{i + 1}.cobertura.xml");

                    File.Copy(
                        coverageReportFiles[i],
                        destination,
                        overwrite: false);
                }

                Console.WriteLine(
                    $"Cobertura reports preserved at: {preservedCoverageDirectory}");
            }
            else
            {
                Console.WriteLine(
                    "Warning: No Cobertura reports were found to preserve.");
            }

            // Step 7: Analyze coverage for the README-selected target file.
            ConsoleProgress.Show(
                "Step 7/7: Analysing target-file line and branch coverage...");

            var lineCoveragePercent = 0.0;
            var branchCoveragePercent = 0.0;
            var lineCoveragePassed = false;
            var branchCoveragePassed = false;

            if (testsPassed)
            {
                diagnostics.AppendLine();
                diagnostics.AppendLine(
                    "Analyzing target-specific Cobertura coverage reports...");

                try
                {
                    var coverageResult =
                        await _codeCoverageService.AnalyzeAsync(
                            workspaceRoot,
                            request.TargetFilePath,
                            cancellationToken);

                    lineCoveragePercent =
                        coverageResult.LineCoveragePercent;

                    branchCoveragePercent =
                        coverageResult.BranchCoveragePercent;

                    lineCoveragePassed =
                        coverageResult.LineCoveragePassed;

                    branchCoveragePassed =
                        coverageResult.BranchCoveragePassed;

                    diagnostics.AppendLine(
                        coverageResult.Diagnostics);

                    diagnostics.AppendLine(
                        $"Target line coverage: {lineCoveragePercent:F2}%");

                    diagnostics.AppendLine(
                        $"Target branch coverage: {branchCoveragePercent:F2}%");

                    diagnostics.AppendLine(
                        $"Line coverage passed: {lineCoveragePassed}");

                    diagnostics.AppendLine(
                        $"Branch coverage passed: {branchCoveragePassed}");
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    diagnostics.AppendLine(
                        $"Coverage analysis failed: {ex.Message}");
                }
            }
            else
            {
                diagnostics.AppendLine(
                    "Coverage analysis skipped because test execution failed.");
            }

            diagnostics.AppendLine();
            diagnostics.AppendLine(
                "Mutation testing has not run yet.");

            diagnostics.AppendLine(
                "Quality and security checks have not run yet.");

            return CreateResult(
                request,
                buildPassed,
                testsPassed,
                diagnostics.ToString(),
                lineCoveragePercent,
                branchCoveragePercent);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return CreateResult(
                request,
                buildPassed: false,
                testsPassed: false,
                $"Validation execution failed: {ex.Message}");
        }
        finally
        {
            // Remove the complete temporary workspace after this attempt.
            // Cleanup must not hide the actual validation result.
            try
            {
                if (Directory.Exists(attemptRoot))
                {
                    Directory.Delete(
                        attemptRoot,
                        recursive: true);
                }
            }
            catch (Exception cleanupException) when (
                cleanupException is IOException or
                UnauthorizedAccessException)
            {
                Console.WriteLine(
                    $"Warning: Temporary workspace cleanup failed: {cleanupException.Message}");
            }
        }
    }

    private static async Task<CommandResult> RunDotnetCommandAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "dotnet",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process
        {
            StartInfo = startInfo
        };

        if (!process.Start())
        {
            throw new InvalidOperationException(
                "Unable to start the dotnet process.");
        }

        var standardOutputTask =
            process.StandardOutput.ReadToEndAsync();

        var standardErrorTask =
            process.StandardError.ReadToEndAsync();

        using var timeoutCts =
            new CancellationTokenSource(CommandTimeout);

        using var linkedCts =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCts.Token);

        try
        {
            await process.WaitForExitAsync(linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync(
                CancellationToken.None);

            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            var timedOutOutput =
                await standardOutputTask +
                Environment.NewLine +
                await standardErrorTask;

            return new CommandResult(
                -1,
                timedOutOutput,
                TimedOut: true);
        }

        var standardOutput =
            await standardOutputTask;

        var standardError =
            await standardErrorTask;

        var output = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(standardOutput))
        {
            output.AppendLine(standardOutput);
        }

        if (!string.IsNullOrWhiteSpace(standardError))
        {
            output.AppendLine("Errors:");
            output.AppendLine(standardError);
        }

        return new CommandResult(
            process.ExitCode,
            output.ToString(),
            TimedOut: false);
    }

    private static ValidationResult CreateResult(
        ValidationRequest request,
        bool buildPassed,
        bool testsPassed,
        string diagnostics,
        double lineCoveragePercent = 0,
        double branchCoveragePercent = 0)
    {
        return new ValidationResult
        {
            RequestId = request.RequestId,
            AttemptNumber = request.AttemptNumber,
            BuildPassed = buildPassed,
            TestsPassed = testsPassed,

            LineCoveragePercent = lineCoveragePercent,
            BranchCoveragePercent = branchCoveragePercent,

            // Compatibility property mirrors line coverage.
            CoveragePercent = lineCoveragePercent,

            // -1 means mutation testing has not run.
            SurvivingMutants = -1,

            // Quality checks are not implemented yet.
            QualityChecksPassed = false,

            Diagnostics = diagnostics
        };
    }

    private static string GetSafeGeneratedTestPath(
        string testProjectRoot,
        string relativeTestFilePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeTestFilePath);

        var normalizedPath = relativeTestFilePath
            .Trim()
            .Replace('\\', '/');

        if (Path.IsPathRooted(normalizedPath) ||
            normalizedPath.Contains(':'))
        {
            throw new InvalidOperationException(
                "Generated test path must be relative.");
        }

        var segments = normalizedPath.Split('/');

        if (segments.Length == 0 ||
            segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".."))
        {
            throw new InvalidOperationException(
                "Generated test path contains an invalid directory segment.");
        }

        if (!normalizedPath.EndsWith(
            ".cs",
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated test file must have a .cs extension.");
        }

        var testRoot = Path.GetFullPath(testProjectRoot);
        var testProjectDirectoryName = Path.GetFileName(testRoot);

        // Remove an unnecessary test-project prefix if Foundry supplies one.
        if (segments.Length > 1 &&
            (segments[0].Equals(
                testProjectDirectoryName,
                StringComparison.OrdinalIgnoreCase) ||
             segments[0].EndsWith(
                ".Tests",
                StringComparison.OrdinalIgnoreCase)))
        {
            segments = segments.Skip(1).ToArray();
        }

        if (segments.Length == 0)
        {
            throw new InvalidOperationException(
                "Generated test path is invalid after normalization.");
        }

        var destination = Path.GetFullPath(
            Path.Combine(testRoot, Path.Combine(segments)));

        var rootWithSeparator =
            testRoot.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(
            rootWithSeparator,
            StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated test file must remain inside the test project.");
        }

        return destination;
    }


    private sealed record CommandResult(
        int ExitCode,
        string Output,
        bool TimedOut);
}
