/*
 * Purpose:
 * Packages the original .NET project with the accepted generated test file.
 *
 * Responsibilities:
 * 1. Download the original project ZIP from Blob Storage.
 * 2. Extract it safely into an isolated temporary workspace.
 * 3. Download the accepted generated test file from Blob Storage.
 * 4. Add the test file under the Tests project without overwriting files.
 * 5. Create a new ZIP and upload it to CodeOutputWithTest.
 *
 * Important:
 * The original input ZIP is never modified.
 * This service must only run after successful AKS validation.
 */

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace TestEngineerPOC.Services;

public sealed class ProjectPackagingService
{
    private const string OutputPrefix = "CodeOutputWithTest";
    private const string TestProjectFile = "Tests/Tests.csproj";

    private readonly BlobStorageClientFactory _blobStorageClientFactory;
    private readonly ILogger<ProjectPackagingService> _logger;

    public ProjectPackagingService(
        BlobStorageClientFactory blobStorageClientFactory,
        ILogger<ProjectPackagingService> logger)
    {
        _blobStorageClientFactory = blobStorageClientFactory;
        _logger = logger;
    }

    public async Task<string> PackageProjectAsync(
        string requestId,
        string containerName,
        string inputBlobName,
        string generatedTestFileBlobName,
        string generatedTestFilePath,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(
            requestId,
            containerName,
            inputBlobName,
            generatedTestFileBlobName,
            generatedTestFilePath);

        var serviceClient =
            await _blobStorageClientFactory.CreateServiceClientAsync(
                cancellationToken);

        var containerClient =
            serviceClient.GetBlobContainerClient(containerName);

        var inputBlobClient =
            containerClient.GetBlobClient(inputBlobName);

        var generatedTestBlobClient =
            containerClient.GetBlobClient(generatedTestFileBlobName);

        if (!await inputBlobClient.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException(
                "Original project ZIP was not found.",
                inputBlobName);
        }

        if (!await generatedTestBlobClient.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException(
                "Accepted generated test file was not found.",
                generatedTestFileBlobName);
        }

        var workspace = Path.Combine(
            Path.GetTempPath(),
            "ProjectPackaging",
            Guid.NewGuid().ToString("N"));

        var extractPath = Path.Combine(workspace, "project");
        var inputZipPath = Path.Combine(workspace, "original.zip");
        var generatedTestTempPath =
            Path.Combine(workspace, "generated-test.cs");
        var outputZipPath = Path.Combine(
            workspace,
            "project-with-tests.zip");

        try
        {
            Directory.CreateDirectory(extractPath);

            _logger.LogInformation(
                "[RequestId={RequestId}] Downloading original project ZIP.",
                requestId);

            await inputBlobClient.DownloadToAsync(
                inputZipPath,
                cancellationToken);

            _logger.LogInformation(
                "[RequestId={RequestId}] Extracting original project.",
                requestId);

            ExtractZipSafely(
                inputZipPath,
                extractPath,
                cancellationToken);

            var testProjectPath = Path.Combine(
                extractPath,
                TestProjectFile.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(testProjectPath))
            {
                throw new FileNotFoundException(
                    $"Expected test project was not found: {TestProjectFile}",
                    testProjectPath);
            }

            var normalizedTestPath =
                NormalizeRelativePath(generatedTestFilePath);

            var testProjectDirectory =
                Path.GetDirectoryName(testProjectPath)!;

            var destinationTestPath = Path.GetFullPath(
                Path.Combine(
                    testProjectDirectory,
                    normalizedTestPath));

            EnsurePathIsInsideDirectory(
                testProjectDirectory,
                destinationTestPath);

            if (File.Exists(destinationTestPath))
            {
                throw new IOException(
                    $"Generated test file already exists. " +
                    $"Existing files will not be overwritten: {normalizedTestPath}");
            }

            var destinationDirectory =
                Path.GetDirectoryName(destinationTestPath)!;

            Directory.CreateDirectory(destinationDirectory);

            _logger.LogInformation(
                "[RequestId={RequestId}] Downloading accepted generated test file.",
                requestId);

            await generatedTestBlobClient.DownloadToAsync(
                generatedTestTempPath,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            File.Copy(
                generatedTestTempPath,
                destinationTestPath,
                overwrite: false);

            _logger.LogInformation(
                "[RequestId={RequestId}] Added generated test file at {TestPath}.",
                requestId,
                normalizedTestPath);

            CreateProjectZip(
                extractPath,
                outputZipPath,
                cancellationToken);

            var outputBlobName =
                $"{OutputPrefix}/{requestId}/project-with-tests.zip";

            var outputBlobClient =
                containerClient.GetBlobClient(outputBlobName);

            await outputBlobClient.UploadAsync(
                outputZipPath,
                overwrite: true,
                cancellationToken);

            _logger.LogInformation(
                "[RequestId={RequestId}] Package uploaded to {OutputBlobName}.",
                requestId,
                outputBlobName);

            return outputBlobName;
        }
        finally
        {
            if (Directory.Exists(workspace))
            {
                try
                {
                    Directory.Delete(workspace, recursive: true);
                }
                catch (IOException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "[RequestId={RequestId}] Could not clean packaging workspace.",
                        requestId);
                }
                catch (UnauthorizedAccessException exception)
                {
                    _logger.LogWarning(
                        exception,
                        "[RequestId={RequestId}] Access denied while cleaning packaging workspace.",
                        requestId);
                }
            }
        }
    }

    private static void ExtractZipSafely(
        string zipPath,
        string destinationDirectory,
        CancellationToken cancellationToken)
    {
        var destinationRoot = Path.GetFullPath(
            destinationDirectory) + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipPath);

        foreach (var entry in archive.Entries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(entry.FullName))
            {
                continue;
            }

            var entryName = entry.FullName.Replace(
                '/',
                Path.DirectorySeparatorChar);

            if (Path.IsPathRooted(entryName))
            {
                throw new InvalidDataException(
                    $"ZIP contains a rooted path: {entry.FullName}");
            }

            var destinationPath = Path.GetFullPath(
                Path.Combine(destinationDirectory, entryName));

            if (!destinationPath.StartsWith(
                    destinationRoot,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ZIP entry attempts to escape the extraction directory: {entry.FullName}");
            }

            if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                entry.FullName.EndsWith("\\", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            var directory = Path.GetDirectoryName(destinationPath);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var sourceStream = entry.Open();

            using var destinationStream = new FileStream(
                destinationPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            sourceStream.CopyTo(destinationStream);
        }
    }

    private static void CreateProjectZip(
        string projectRoot,
        string outputZipPath,
        CancellationToken cancellationToken)
    {
        var files = Directory.GetFiles(
            projectRoot,
            "*",
            SearchOption.AllDirectories)
            .Where(path => !IsExcludedPath(projectRoot, path))
            .ToArray();

        if (files.Length == 0)
        {
            throw new InvalidOperationException(
                "No project files were found to package.");
        }

        using var archive = ZipFile.Open(
            outputZipPath,
            ZipArchiveMode.Create);

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath = Path.GetRelativePath(
                projectRoot,
                filePath);

            archive.CreateEntryFromFile(
                filePath,
                relativePath.Replace('\\', '/'),
                CompressionLevel.Optimal);
        }
    }

    private static bool IsExcludedPath(
        string projectRoot,
        string filePath)
    {
        var relativePath = Path.GetRelativePath(
            projectRoot,
            filePath);

        var segments = relativePath.Split(
            new[]
            {
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar
            },
            StringSplitOptions.RemoveEmptyEntries);

        return segments.Any(segment =>
            segment.Equals("bin", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals("obj", StringComparison.OrdinalIgnoreCase) ||
            segment.Equals(".git", StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "Generated test file path is required.",
                nameof(path));
        }

        var normalized = path
            .Replace('\\', '/')
            .Trim();

        if (normalized.StartsWith("/", StringComparison.Ordinal) ||
            Path.IsPathRooted(normalized) ||
            normalized.Split('/').Any(
                segment => segment is "." or ".." or ""))
        {
            throw new ArgumentException(
                "Generated test file path must be a safe relative path.",
                nameof(path));
        }

        if (!normalized.EndsWith(
                ".cs",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Generated test file must have a .cs extension.",
                nameof(path));
        }

        return normalized.Replace(
            '/',
            Path.DirectorySeparatorChar);
    }

    private static void EnsurePathIsInsideDirectory(
        string rootDirectory,
        string destinationPath)
    {
        var root = Path.GetFullPath(rootDirectory)
            + Path.DirectorySeparatorChar;

        var fullDestination = Path.GetFullPath(destinationPath);

        if (!fullDestination.StartsWith(
                root,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Generated test file path is outside the test project.");
        }
    }

    private static void ValidateRequest(
        string requestId,
        string containerName,
        string inputBlobName,
        string generatedTestFileBlobName,
        string generatedTestFilePath)
    {
        if (string.IsNullOrWhiteSpace(requestId) ||
            requestId.Contains('/') ||
            requestId.Contains('\\') ||
            requestId.Contains(".."))
        {
            throw new ArgumentException(
                "A valid request ID is required.",
                nameof(requestId));
        }

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new ArgumentException(
                "Container name is required.",
                nameof(containerName));
        }

        ValidateBlobName(inputBlobName, nameof(inputBlobName));
        ValidateBlobName(
            generatedTestFileBlobName,
            nameof(generatedTestFileBlobName));

        _ = NormalizeRelativePath(generatedTestFilePath);
    }

    private static void ValidateBlobName(
        string blobName,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(blobName) ||
            blobName.Contains('\\') ||
            blobName.Split('/').Any(
                segment => segment is "." or ".." or ""))
        {
            throw new ArgumentException(
                "A valid Blob name is required.",
                parameterName);
        }
    }
}