/*
 * Purpose:
 * Provides the entry point for the Test Engineer validation worker.
 * The worker executes one validation attempt and returns a ValidationResult.
 *
 * Usage:
 * - Local development:
 *   Receives ValidationRequest JSON through standard input and writes
 *   ValidationResult JSON to standard output.
 *
 * - Cloud execution:
 *   Reads ValidationRequest JSON from Blob Storage and uploads
 *   ValidationResult JSON to Blob Storage.
 *
 * Configuration:
 * - Local development: appsettings.json / appsettings.Development.json
 * - AKS deployment: Environment variables
 * - Storage configuration:
 *   StorageAccount
 *   StorageAccount__ContainerName
 * - Cloud request/result transport:
 *   Worker__RequestBlobName and Worker__ResultBlobName environment variables
 *
 * Note:
 * The worker performs only one validation attempt.
 * The Function App is responsible for the Ralph retry loop.
 */

using System.Text.Json;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TestEngineer.Contracts;
using TestEngineerWorker.Options;
using TestEngineerWorker.Services;
using TestEngineerWorker.Workers;

// Environment variable names used for cloud request/result transport.
const string RequestBlobSetting = "Worker__RequestBlobName";
const string ResultBlobSetting = "Worker__ResultBlobName";

// Determine the current environment.
var environment =
    Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT")
    ?? "Development";

// ------------------------------------------------------------
// Stage 1: Load application configuration.
// Local JSON configuration is loaded first.
// Environment variables override JSON configuration values.
// ------------------------------------------------------------
var configuration = new ConfigurationBuilder()
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile(
        "appsettings.json",
        optional: true,
        reloadOnChange: false)
    .AddJsonFile(
        $"appsettings.{environment}.json",
        optional: true,
        reloadOnChange: false)
    .AddEnvironmentVariables()
    .Build();

// ------------------------------------------------------------
// Stage 2: Register worker dependencies.
// ------------------------------------------------------------
var services = new ServiceCollection();

services.AddSingleton<IConfiguration>(configuration);

// Bind the common StorageAccount configuration.
// StorageAccount:
//   Local  = connection string
//   Azure  = HTTPS Blob Storage endpoint
//
// StorageAccount__ContainerName:
//   Container name, for example "osaas".
services.Configure<BlobStorageOptions>(options =>
{
    options.StorageAccount =
        configuration["StorageAccount"] ?? string.Empty;

    options.ContainerName =
        configuration["StorageAccount__ContainerName"] ?? string.Empty;
});

services.AddSingleton<IBlobProjectDownloader, BlobProjectDownloader>();
services.AddSingleton<IWorkspaceService, WorkspaceService>();
services.AddSingleton<IProjectDiscoveryService, ProjectDiscoveryService>();
services.AddSingleton<ICodeCoverageService, CodeCoverageService>();

services.AddTransient<ValidationWorker>();

using var serviceProvider = services.BuildServiceProvider();

// JSON deserialization options.
var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};

// Reuse the same Blob Storage container client for cloud request/result
// operations during this worker invocation.
BlobContainerClient? cloudContainerClient = null;

try
{
    // ------------------------------------------------------------
    // Stage 3: Determine the execution mode.
    // If either cloud blob setting is supplied, both are required.
    // Otherwise, use the existing local standard-input workflow.
    // ------------------------------------------------------------
    var requestBlobName =
        Environment.GetEnvironmentVariable(RequestBlobSetting);

    var resultBlobName =
        Environment.GetEnvironmentVariable(ResultBlobSetting);

    var cloudMode =
        !string.IsNullOrWhiteSpace(requestBlobName) ||
        !string.IsNullOrWhiteSpace(resultBlobName);

    string requestJson;

    if (cloudMode)
    {
        // Both request and result blob names are mandatory in cloud mode.
        if (string.IsNullOrWhiteSpace(requestBlobName) ||
            string.IsNullOrWhiteSpace(resultBlobName))
        {
            throw new InvalidOperationException(
                "Both Worker__RequestBlobName and " +
                "Worker__ResultBlobName must be configured.");
        }

        // Restrict request and result blobs to their expected folders.
        ValidateBlobName(
            requestBlobName,
            "ValidationRequests/");

        ValidateBlobName(
            resultBlobName,
            "ValidationResults/");

        // Load Blob Storage settings.
        var blobOptions = serviceProvider
            .GetRequiredService<IOptions<BlobStorageOptions>>()
            .Value;

        if (string.IsNullOrWhiteSpace(blobOptions.ContainerName))
        {
            throw new InvalidOperationException(
                "Blob Storage container name is not configured.");
        }

        // Create the Blob Storage client once and reuse it.
        var blobServiceClient = CreateBlobServiceClient(blobOptions);

        cloudContainerClient = blobServiceClient.GetBlobContainerClient(
            blobOptions.ContainerName);

        // Read the validation request JSON from Blob Storage.
        Console.Error.WriteLine(
            $"Reading validation request: {requestBlobName}");

        var requestBlob = cloudContainerClient.GetBlobClient(
            requestBlobName);

        var requestContent = await requestBlob.DownloadContentAsync();

        requestJson = requestContent.Value.Content.ToString();
    }
    else
    {
        // Local development remains compatible with:
        // Get-Content .\request.json -Raw |
        //     dotnet run --project .\TestEngineerWorker\TestEngineerWorker.csproj
        requestJson = await Console.In.ReadToEndAsync();
    }

    // ------------------------------------------------------------
    // Stage 4: Validate and deserialize the incoming request.
    // ------------------------------------------------------------
    if (string.IsNullOrWhiteSpace(requestJson))
    {
        throw new InvalidOperationException(
            "A validation request JSON is required.");
    }

    var request = JsonSerializer.Deserialize<ValidationRequest>(
        requestJson,
        jsonOptions);

    if (request is null)
    {
        throw new JsonException(
            "The validation request is invalid.");
    }

    // ------------------------------------------------------------
    // Stage 5: Execute one validation attempt.
    // ValidationWorker handles project download, workspace creation,
    // build, test execution, and coverage analysis.
    // ------------------------------------------------------------
    Console.Error.WriteLine(
        $"Starting validation. RequestId: {request.RequestId}, " +
        $"Attempt: {request.AttemptNumber}");

    var worker = serviceProvider
        .GetRequiredService<ValidationWorker>();

    var result = await worker.ValidateAsync(request);

    // Serialize the validation result.
    var resultJson = JsonSerializer.Serialize(
        result,
        jsonOptions);

    // ------------------------------------------------------------
    // Stage 6: Publish the result in cloud mode.
    // Do not overwrite an existing result blob.
    // ------------------------------------------------------------
    if (cloudMode)
    {
        var resultBlob = cloudContainerClient!.GetBlobClient(
            resultBlobName!);

        await resultBlob.UploadAsync(
            BinaryData.FromString(resultJson),
            overwrite: false);

        Console.Error.WriteLine(
            $"Validation result uploaded: {resultBlobName}");

        // A failed validation is a business outcome.
        // The Function App reads Passed and decides whether to retry.
        // Exit code 0 means the worker completed and published its result.
        Console.WriteLine(resultJson);

        return 0;
    }

    // ------------------------------------------------------------
    // Stage 7: Return the result in local development mode.
    // A failed validation returns exit code 1 locally.
    // ------------------------------------------------------------
    Console.WriteLine(resultJson);

    return result.Passed ? 0 : 1;
}
catch (JsonException exception)
{
    // Invalid request or JSON deserialization failure.
    Console.Error.WriteLine(
        $"Invalid request JSON: {exception.Message}");

    return 1;
}
catch (ArgumentException exception)
{
    // Invalid validation request parameters.
    Console.Error.WriteLine(
        $"Validation request error: {exception.Message}");

    return 1;
}
catch (Exception exception)
{
    // Unexpected worker, Blob Storage, or infrastructure failure.
    Console.Error.WriteLine(
        $"Validation worker failed: {exception}");

    return 1;
}

// ------------------------------------------------------------
// Creates a BlobServiceClient using the common StorageAccount setting.
//
// Local development:
// StorageAccount contains a connection string.
//
// Cloud execution:
// StorageAccount contains an HTTPS Blob Storage endpoint.
// DefaultAzureCredential uses the available Azure identity,
// including AKS Workload Identity when configured.
// ------------------------------------------------------------
static BlobServiceClient CreateBlobServiceClient(
    BlobStorageOptions options)
{
    var storageAccount = options.StorageAccount;

    if (string.IsNullOrWhiteSpace(storageAccount))
    {
        throw new InvalidOperationException(
            "StorageAccount is not configured.");
    }

    // Local development: use the configured connection string.
    if (storageAccount.StartsWith(
            "DefaultEndpointsProtocol=",
            StringComparison.OrdinalIgnoreCase) ||
        storageAccount.StartsWith(
            "UseDevelopmentStorage=",
            StringComparison.OrdinalIgnoreCase))
    {
        return new BlobServiceClient(storageAccount);
    }

    // Azure-hosted execution: use the HTTPS service endpoint and
    // DefaultAzureCredential.
    if (!Uri.TryCreate(
            storageAccount,
            UriKind.Absolute,
            out var serviceUri) ||
        serviceUri.Scheme != Uri.UriSchemeHttps)
    {
        throw new InvalidOperationException(
            "StorageAccount must contain a valid connection string " +
            "or an HTTPS Blob Storage endpoint.");
    }

    return new BlobServiceClient(
        serviceUri,
        new DefaultAzureCredential());
}

// ------------------------------------------------------------
// Validates that a blob name belongs to its expected folder.
// Rejects backslashes, colons, empty path segments, and
// relative path segments.
// ------------------------------------------------------------
static void ValidateBlobName(
    string blobName,
    string requiredPrefix)
{
    if (string.IsNullOrWhiteSpace(blobName) ||
        !blobName.StartsWith(
            requiredPrefix,
            StringComparison.Ordinal) ||
        blobName.Contains('\\') ||
        blobName.Contains(':') ||
        blobName.Split('/').Any(
            segment => segment is "." or ".." or ""))
    {
        throw new InvalidOperationException(
            $"Invalid worker blob path. Expected prefix: {requiredPrefix}");
    }
}
