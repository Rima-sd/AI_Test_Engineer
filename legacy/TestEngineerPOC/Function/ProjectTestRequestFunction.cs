
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Xml.Linq;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Services;
using System.Text.RegularExpressions;
using Microsoft.DurableTask.Client;

namespace TestEngineerPOC.Function;

public sealed class ProjectTestRequestFunction
{
    private readonly ILogger<ProjectTestRequestFunction> _logger;
    private readonly TargetInventoryService _targetInventoryService;
    private readonly TargetDependencyService _targetDependencyService;
    private readonly DependencyInventoryService _dependencyInventoryService;
    private readonly TestGenerationContextService _testGenerationContextService;
    private readonly BlobStorageClientFactory _blobStorageClientFactory;

    public ProjectTestRequestFunction(
       ILogger<ProjectTestRequestFunction> logger,
       TargetInventoryService targetInventoryService,
       TargetDependencyService targetDependencyService,
       DependencyInventoryService dependencyInventoryService,
       TestGenerationContextService testGenerationContextService,
        BlobStorageClientFactory blobStorageClientFactory)
    {
        _logger = logger;
        _targetInventoryService = targetInventoryService;
        _targetDependencyService = targetDependencyService;
        _dependencyInventoryService = dependencyInventoryService;
        _testGenerationContextService = testGenerationContextService;
        _blobStorageClientFactory = blobStorageClientFactory;
    }

    public async Task Run(
        [ServiceBusTrigger(
        "top-project-test-requests",
        "ai-test-engineer-sub",
        Connection = "ServiceBusConnection")]
    string message,
        [DurableClient] DurableTaskClient durableClient)
    {
        // Extract the request ID before deserialization so that even
        // malformed payloads can be traced in Application Insights.
        var requestId = TryGetRequestId(message)
            ?? $"unknown-{Guid.NewGuid():N}";

        string? workDirectory = null;

        try
        {
            // STAGE 1: Record the incoming request.
            // Payload logging is bounded to avoid excessively large logs.
            const int maxPayloadLogLength = 8000;

            var loggedPayload = message.Length <= maxPayloadLogLength
                ? message
                : message[..maxPayloadLogLength] + "... [truncated]";

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=PayloadReceived] " +
                "PayloadLength={PayloadLength}; Payload={Payload}",
                requestId,
                message.Length,
                loggedPayload);

            // STAGE 2: Deserialize and validate the message contract.
            var request = JsonSerializer.Deserialize<ProjectTestRequest>(
                message,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

            if (request is null)
            {
                throw new JsonException("Request payload is empty.");
            }

            // Use the validated request ID for all subsequent logs.
            requestId = request.RequestId?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(requestId) ||
                string.IsNullOrWhiteSpace(request.ContainerName) ||
                string.IsNullOrWhiteSpace(request.BlobName) ||
                string.IsNullOrWhiteSpace(request.ReadmePath))
            {
                throw new JsonException(
                    "Required fields are missing: requestId, " +
                    "containerName, blobName, or readmePath.");
            }

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ValidationStarted] " +
                "Validating container and blob path.",
                requestId);

            var configuredContainer =
     _blobStorageClientFactory.GetRequiredContainerName();

            _logger.LogInformation(
                "[RequestId={RequestId}] ConfiguredContainer={ConfiguredContainer}; " +
                "MessageContainer={MessageContainer}; BlobName={BlobName}",
                requestId,
                configuredContainer,
                request.ContainerName,
                request.BlobName);

            if (!string.Equals(
                    request.ContainerName,
                    configuredContainer,
                    StringComparison.OrdinalIgnoreCase) ||
                !request.BlobName.StartsWith(
                    "CodeOutput/",
                    StringComparison.Ordinal) ||
                request.BlobName.Contains('\\') ||
                request.BlobName.Split('/').Any(
                    segment => segment is "." or "..") ||
                request.BlobName.Contains(':'))
            {
                throw new InvalidOperationException("Container or input blob path is not permitted.");
            }

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ValidationCompleted] " +
                "Container={ContainerName}; Blob={BlobName}; " +
                "ReadmePath={ReadmePath}",
                requestId,
                request.ContainerName,
                request.BlobName,
                request.ReadmePath);


            // STAGE 3: Create Blob Storage client using the configured
            // Managed Identity / connection-string authentication strategy.
            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=BlobStorageClientCreationStarted] " +
                "Creating Blob Storage client.",
                requestId);

            var serviceClient =
                await _blobStorageClientFactory.CreateServiceClientAsync();

            var containerClient = serviceClient.GetBlobContainerClient(
                _blobStorageClientFactory.GetRequiredContainerName());

            var blobClient = containerClient.GetBlobClient(request.BlobName);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=BlobStorageClientCreated] " +
                "Blob Storage client created successfully.",
                requestId);


            // Create a unique workspace for this invocation.
            // Never use an incoming filename as a local filesystem path.
            workDirectory = Path.Combine(
                Path.GetTempPath(),
                "ai-test-engineer",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(workDirectory);

            var localZipPath = Path.Combine(
                workDirectory,
                "input.zip");

            // STAGE 4: Download the original ZIP.
            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DownloadStarted] " +
                "Downloading input ZIP from Blob Storage.",
                requestId);

            await blobClient.DownloadToAsync(localZipPath);

            var fileInfo = new FileInfo(localZipPath);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DownloadCompleted] " +
                "Blob={BlobName}; SizeBytes={SizeBytes}; " +
                "TemporaryPath={TemporaryPath}",
                requestId,
                request.BlobName,
                fileInfo.Length,
                localZipPath);

            // STAGE 5: Safely extract into a separate temporary directory.
            // The original ZIP in Blob Storage is never modified.
            var extractionDirectory = Path.Combine(workDirectory, "extracted");

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ExtractionStarted] " +
                "Starting safe ZIP extraction.",
                requestId);

            var extractedFileCount = ExtractZipSafely(localZipPath, extractionDirectory);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ExtractionCompleted] " +
                "ExtractedFiles={ExtractedFileCount}; " +
                "Directory={Directory}",
                requestId,
                extractedFileCount,
                extractionDirectory);

            // STAGE 6: Locate and read the README.
            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ReadmeDiscoveryStarted] " +
                "Looking for README path {ReadmePath}.",
                requestId,
                request.ReadmePath);

            var readme = FindAndReadReadme(
                extractionDirectory,
                request.ReadmePath);

            // Determine the project root from the README location.
            var projectRoot = Path.GetDirectoryName(readme.FullPath)
                ?? throw new InvalidOperationException(
                    "Could not determine the project root from the README path.");

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=ReadmeReadCompleted] " +
                "ReadmePath={ReadmePath}; ContentLength={ContentLength}; " +
                "Content={Content}",
                requestId,
                readme.RelativePath,
                readme.Content.Length,
                TruncateForLog(readme.Content, 8000));

            // STAGE 7: Resolve the unit-test target from the existing README.
            // The Mapping and DTO customization section identifies the source file.
            // No target path is hardcoded in the Function.

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=TargetDiscoveryStarted] " +
                "Resolving target from README.",
                requestId);

            var targetRelativePath = ResolveTargetFromReadme(
                readme.Content,
                projectRoot);

            var targetFilePath = Path.GetFullPath(
                Path.Combine(
                    projectRoot,
                    targetRelativePath.Replace('/', Path.DirectorySeparatorChar)));

            var resolvedTargetRelativePath = Path.GetRelativePath(
                    extractionDirectory,
                    targetFilePath)
                .Replace('\\', '/');

            var targetFileInfo = new FileInfo(targetFilePath);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=TargetDiscoveryCompleted] " +
                "TargetPath={TargetPath}; SizeBytes={SizeBytes}",
                requestId,
                resolvedTargetRelativePath,
                targetFileInfo.Length);

            // STAGE 8: Build the target source inventory.
            var inventory = _targetInventoryService.CreateInventory(
                requestId,
                targetFilePath,
                resolvedTargetRelativePath);

            _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=TargetInventoryAccepted] " +
            "TargetPath={TargetPath}; TypeCount={TypeCount}; MethodCount={MethodCount}",
            requestId,
            inventory.TargetPath,
            inventory.TypeNames.Count,
            inventory.MethodNames.Count);

            // STAGE 9: Discover related source files.
            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DependencyDiscoveryStarted] " +
                "TargetPath={TargetPath}; ProjectRoot={ProjectRoot}",
                requestId,
                resolvedTargetRelativePath,
                projectRoot);

            var dependencies = _targetDependencyService.DiscoverDependencies(
                requestId,
                targetFilePath,
                projectRoot);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DependencyDiscoveryAccepted] " +
                "DependencyCount={DependencyCount}; Dependencies={Dependencies}",
                requestId,
                dependencies.Count,
                string.Join("; ", dependencies));

            // STAGE 10: Build inventories for discovered dependency files.
            var dependencyInventories =
                _dependencyInventoryService.CreateInventories(
                    requestId,
                    projectRoot,
                    dependencies);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DependencyInventoriesAccepted] " +
                "InventoryCount={InventoryCount}",
                requestId,
                dependencyInventories.Count);

            // STAGE 11: Prepare structured context for unit-test generation.
            var testGenerationContext = _testGenerationContextService.CreateContext(
                requestId,
                targetFilePath,
                resolvedTargetRelativePath,
                readme.Content,
                inventory,
                dependencyInventories,
                request.AttemptNumber,
                request.PreviousDiagnostics);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=TestGenerationContextAccepted] " +
                "TargetPath={TargetPath}; AttemptNumber={AttemptNumber}; " +
                "DependencyCount={DependencyCount}; SourceLength={SourceLength}; " +
                "ReadmeLength={ReadmeLength}; PreviousDiagnosticsLength={DiagnosticsLength}",
                requestId,
                testGenerationContext.TargetFilePath,
                testGenerationContext.AttemptNumber,
                testGenerationContext.Dependencies.Count,
                testGenerationContext.TargetSourceCode.Length,
                testGenerationContext.ReadmeContent.Length,
                testGenerationContext.PreviousDiagnostics.Length);


            // STAGE 12: Start Durable orchestration.
            // Foundry generation, AKS validation, retries and packaging
            // are handled by TestGenerationOrchestrator.

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DurableOrchestrationStarting] " +
                "Starting TestGenerationOrchestrator.",
                requestId);

            var orchestrationInput = new TestGenerationOrchestrationInput
            {
                GenerationContext = testGenerationContext,
                InputContainerName = request.ContainerName,
                InputBlobName = request.BlobName
            };

            var instanceId = await durableClient.ScheduleNewOrchestrationInstanceAsync(
                nameof(TestGenerationOrchestrator),
                orchestrationInput);

            _logger.LogInformation(
                "[RequestId={RequestId}] [Stage=DurableOrchestrationStarted] " +
                "OrchestrationInstanceId={InstanceId}",
                requestId,
                instanceId);
        }
        catch (Exception ex)
        {
            // Every processing failure includes the same searchable ID.
            // Preserve the exception details for diagnosis.
            _logger.LogError(
                ex,
                "[RequestId={RequestId}] [Stage=Failed] " +
                "Request processing failed. ExceptionType={ExceptionType}",
                requestId,
                ex.GetType().Name);

            // Rethrow so Azure Functions/Service Bus retry and
            // dead-letter behavior can handle the failed invocation.
            throw;
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(workDirectory) &&
                Directory.Exists(workDirectory))
            {
                try
                {
                    Directory.Delete(workDirectory, recursive: true);

                    _logger.LogInformation(
                        "[RequestId={RequestId}] [Stage=CleanupCompleted] " +
                        "Temporary workspace deleted.",
                        requestId);
                }
                catch (Exception cleanupException)
                {
                    _logger.LogWarning(
                        cleanupException,
                        "[RequestId={RequestId}] [Stage=CleanupFailed] " +
                        "Could not delete temporary workspace.",
                        requestId);
                }
            }
        }
    }


    /// <summary>
    /// Finds the single existing test project in the extracted source tree.
    /// A project is considered a test project when it declares IsTestProject
    /// or references Microsoft.NET.Test.Sdk.
    /// </summary>
    private static string FindTestProjectRoot(string extractionDirectory)
    {
        var candidates = Directory
            .EnumerateFiles(
                extractionDirectory,
                "*.csproj",
                SearchOption.AllDirectories)
            .Where(path =>
            {
                var segments = Path.GetRelativePath(extractionDirectory, path)
                    .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                return !segments.Any(segment =>
                    segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
                    segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
            })
            .Where(IsTestProject)
            .Select(Path.GetFullPath)
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "No test project was found. Expected a project declaring " +
                "IsTestProject or referencing Microsoft.NET.Test.Sdk.");
        }

        if (candidates.Count > 1)
        {
            throw new InvalidOperationException(
                "Multiple test projects were found. The POC currently requires " +
                "one unambiguous test project. Found: " +
                string.Join("; ", candidates));
        }

        return Path.GetDirectoryName(candidates[0])!;
    }

    private static bool IsTestProject(string projectFile)
    {
        try
        {
            var document = XDocument.Load(projectFile);

            return document
                .Descendants()
                .Any(element =>
                    (element.Name.LocalName.Equals(
                        "IsTestProject",
                        StringComparison.OrdinalIgnoreCase) &&
                     bool.TryParse(element.Value, out var isTest) && isTest) ||
                    (element.Name.LocalName.Equals(
                        "PackageReference",
                        StringComparison.OrdinalIgnoreCase) &&
                     string.Equals(
                         (string?)element.Attribute("Include"),
                         "Microsoft.NET.Test.Sdk",
                         StringComparison.OrdinalIgnoreCase)));
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
        {
            throw new InvalidDataException(
                $"Could not inspect project file '{projectFile}'.", ex);
        }
    }

    /// <summary>
    /// Finds the requested README inside the extracted project and reads it.
    /// A root-level README is preferred. If the requested file cannot be
    /// uniquely identified, processing fails instead of selecting arbitrarily.
    /// </summary>
    private static (string FullPath, string RelativePath, string Content) FindAndReadReadme(
        string extractionDirectory, string requestedReadmePath)
    {
        if (string.IsNullOrWhiteSpace(requestedReadmePath) ||
            Path.IsPathRooted(requestedReadmePath) ||
            requestedReadmePath.Contains(':'))
        {
            throw new InvalidDataException("The requested README path is invalid.");
        }

        // Normalize separators and reject traversal components.
        var normalizedPath = requestedReadmePath.Replace('\\', '/');
        var pathSegments = normalizedPath.Split(
            '/', StringSplitOptions.RemoveEmptyEntries);

        if (pathSegments.Length == 0 ||
            pathSegments.Any(segment => segment is "." or ".."))
        {
            throw new InvalidDataException("The requested README path contains unsafe segments.");
        }

        var root = Path.GetFullPath(extractionDirectory);

        // First try the exact relative path under the extraction root.
        var exactPath = Path.GetFullPath(Path.Combine(root, Path.Combine(pathSegments)));

        var rootPrefix = root.TrimEnd(Path.DirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!exactPath.StartsWith(rootPrefix, StringComparison.Ordinal) &&
            !string.Equals(exactPath, root, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The requested README path escapes the extraction directory.");
        }

        string selectedPath;

        if (File.Exists(exactPath))
        {
            selectedPath = exactPath;
        }
        else
        {
            // ZIPs sometimes contain a top-level project folder.
            // In that case, search by the requested filename.
            var requestedFileName = pathSegments[^1];

            var candidates = Directory
                .EnumerateFiles(
                    root,
                    requestedFileName,
                    SearchOption.AllDirectories)
                .Select(Path.GetFullPath)
                .ToList();

            if (candidates.Count == 0)
            {
                throw new FileNotFoundException($"README '{requestedReadmePath}' was not found.");
            }

            // Prefer a README directly at the extraction root.
            var rootCandidates = candidates
                .Where(path => string.Equals(
                    Path.GetDirectoryName(path),
                    root,
                    StringComparison.Ordinal))
                .ToList();

            if (rootCandidates.Count == 1)
            {
                selectedPath = rootCandidates[0];
            }
            else if (rootCandidates.Count > 1 || candidates.Count > 1)
            {
                throw new InvalidDataException(
                    $"Multiple README candidates were found for " +
                    $"'{requestedReadmePath}'. Specify a more precise path.");
            }
            else
            {
                selectedPath = candidates[0];
            }
        }

        // Limit README size before loading it into memory.
        const long MaxReadmeBytes = 1024 * 1024;

        var fileInfo = new FileInfo(selectedPath);

        if (fileInfo.Length > MaxReadmeBytes)
        {
            throw new InvalidDataException("README exceeds the 1 MB processing limit.");
        }

        var content = File.ReadAllText(selectedPath);

        var relativePath = Path.GetRelativePath(root, selectedPath).Replace('\\', '/');

        return (selectedPath, relativePath, content);
    }

    /// <summary>
    /// Keeps log messages bounded so large README files don't flood telemetry.
    /// </summary>
    private static string TruncateForLog(string content, int maxLength)
    {
        if (content.Length <= maxLength)
        {
            return content;
        }

        return content[..maxLength] + "... [truncated]";
    }


    /// <summary>
    /// Attempts to extract requestId from the payload before full
    /// deserialization. Returns null if the JSON is invalid or the
    /// property is missing.
    /// </summary>
    private static string? TryGetRequestId(string message)
    {
        try
        {
            using var document = JsonDocument.Parse(message);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(
                        property.Name,
                        "requestId",
                        StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    var value = property.Value.GetString();

                    return string.IsNullOrWhiteSpace(value)
                        ? null
                        : value.Trim();
                }
            }
        }
        catch (JsonException)
        {
            // The main processing method will log the parsing failure.
        }

        return null;
    }

    /// <summary>
    /// Extracts ZIP entries only after validating paths and archive limits.
    /// Rejects traversal paths, symbolic links, duplicate file paths,
    /// excessive entry counts, and oversized archives.
    /// </summary>
    private static int ExtractZipSafely(string zipPath, string destinationDirectory)
    {
        const int MaxEntries = 5000;
        const long MaxTotalUncompressedBytes = 500L * 1024 * 1024;
        const long MaxSingleFileBytes = 100L * 1024 * 1024;

        var root = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(root);

        var rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        if (archive.Entries.Count > MaxEntries)
        {
            throw new InvalidDataException("ZIP contains too many entries.");
        }

        long totalBytes = 0;

        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Validate every entry before writing extracted files.
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName;

            if (string.IsNullOrWhiteSpace(name) ||
                name.Contains('\\') ||
                name.StartsWith('/') ||
                name.Contains(':'))
            {
                throw new InvalidDataException($"Unsafe ZIP entry path: {name}");
            }

            var isDirectory = name.EndsWith('/');

            var segments = name.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 ||
                segments.Any(segment =>
                    segment is "." or ".."))
            {
                throw new InvalidDataException($"Unsafe ZIP entry path: {name}");
            }

            var targetPath = Path.GetFullPath(
                Path.Combine(
                    root,
                    Path.Combine(segments)));

            if (!targetPath.StartsWith(
                    rootPrefix,
                    StringComparison.Ordinal) &&
                !string.Equals(
                    targetPath,
                    root,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException($"ZIP entry escapes extraction directory: {name}");
            }

            // Reject symbolic links instead of following them.
            var unixFileType = (entry.ExternalAttributes >> 16) & 0xF000;

            if (unixFileType == 0xA000)
            {
                throw new InvalidDataException($"Symbolic links are not allowed: {name}");
            }

            if (!isDirectory)
            {
                if (!seenPaths.Add(targetPath))
                {
                    throw new InvalidDataException($"Duplicate ZIP entry path: {name}");
                }

                if (entry.Length > MaxSingleFileBytes)
                {
                    throw new InvalidDataException($"ZIP entry exceeds the per-file size limit: {name}");
                }

                totalBytes = checked(totalBytes + entry.Length);

                if (totalBytes > MaxTotalUncompressedBytes)
                {
                    throw new InvalidDataException("ZIP exceeds the total extraction size limit.");
                }
            }
        }

        var extractedFiles = 0;

        // Extract only after all entries pass validation.
        foreach (var entry in archive.Entries)
        {
            var segments = entry.FullName.Split(
                '/',
                StringSplitOptions.RemoveEmptyEntries);

            var targetPath = Path.GetFullPath(Path.Combine(root, Path.Combine(segments)));

            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);

            using var input = entry.Open();

            using var output = new FileStream(
                targetPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            input.CopyTo(output);
            extractedFiles++;
        }

        return extractedFiles;
    }

    /// <summary>
    /// Resolves the unit-test target from the existing README.
    /// Reads C# source paths from the Mapping and DTO customization section.
    /// Resolves paths relative to the directory containing the README.
    /// </summary>
    private static string ResolveTargetFromReadme(
        string readmeContent,
        string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(readmeContent))
        {
            throw new InvalidDataException(
                "README content is empty.");
        }

        // Locate the existing README section describing mapping customization.
        var sectionMatch = Regex.Match(
            readmeContent,
            @"^##\s+Mapping and DTO customization\s*$([\s\S]*?)(?=^##\s|\z)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        if (!sectionMatch.Success)
        {
            throw new InvalidDataException(
                "README does not contain the " +
                "'Mapping and DTO customization' section.");
        }

        // Extract relative C# paths written in Markdown code formatting.
        var pathMatches = Regex.Matches(
            sectionMatch.Groups[1].Value,
            @"`([^`]+\.cs)`",
            RegexOptions.IgnoreCase);

        var targetPaths = pathMatches
            .Select(match => match.Groups[1].Value.Trim())
            .Where(path => path.Contains('/') || path.Contains('\\'))
            .Select(path => path.Replace('\\', '/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (targetPaths.Count != 1)
        {
            throw new InvalidDataException(
                "Expected exactly one C# target path in the README's " +
                "'Mapping and DTO customization' section. " +
                $"Found {targetPaths.Count}: {string.Join(", ", targetPaths)}");
        }

        var relativePath = targetPaths[0];

        if (Path.IsPathRooted(relativePath) ||
            relativePath.Contains(':'))
        {
            throw new InvalidDataException(
                $"README target path is not relative: {relativePath}");
        }

        var segments = relativePath.Split('/');

        if (segments.Any(segment =>
            string.IsNullOrWhiteSpace(segment) ||
            segment is "." or ".."))
        {
            throw new InvalidDataException(
                $"README target path contains invalid segments: {relativePath}");
        }

        var fullProjectRoot = Path.GetFullPath(projectRoot);

        var fullTargetPath = Path.GetFullPath(
            Path.Combine(
                fullProjectRoot,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

        var relativeToProjectRoot = Path.GetRelativePath(
            fullProjectRoot,
            fullTargetPath);

        if (Path.IsPathRooted(relativeToProjectRoot) ||
            relativeToProjectRoot == ".." ||
            relativeToProjectRoot.StartsWith(
                ".." + Path.DirectorySeparatorChar,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "README target path escapes the project directory.");
        }

        if (!File.Exists(fullTargetPath))
        {
            throw new FileNotFoundException(
                $"README-selected target file was not found: {relativePath}",
                fullTargetPath);
        }

        return relativePath;
    }
}


/// <summary>
/// JSON contract for a project test request sent through Service Bus.
/// </summary>
public sealed class ProjectTestRequest
{
    public string RequestId { get; set; } = string.Empty;
    public string ContainerName { get; set; } = string.Empty;
    public string BlobName { get; set; } = string.Empty;
    public string ReadmePath { get; set; } = string.Empty;
    // Ralph loop attempt number.
    // Existing Service Bus messages default to attempt 1.
    public int AttemptNumber { get; set; } = 1;

    // Validation feedback from the previous attempt.
    public string PreviousDiagnostics { get; set; } = string.Empty;
}
