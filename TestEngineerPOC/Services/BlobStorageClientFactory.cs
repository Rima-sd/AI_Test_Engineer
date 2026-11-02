
using Azure.Core;
using Azure.Identity;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using TestEngineerPOC.Utils;

namespace TestEngineerPOC.Services;

public sealed class BlobStorageClientFactory
{
    private const string StorageScope =
        "https://storage.azure.com/.default";

    private readonly IConfiguration _configuration;
    private readonly ILogger<BlobStorageClientFactory> _logger;

    public BlobStorageClientFactory(
        IConfiguration configuration,
        ILogger<BlobStorageClientFactory> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<BlobServiceClient> CreateServiceClientAsync(
        CancellationToken cancellationToken = default)
    {
        var storageAccount =
            _configuration[AppConstants.StorageAccount];

        if (string.IsNullOrWhiteSpace(storageAccount))
        {
            throw new InvalidOperationException(
                "StorageAccount configuration is missing.");
        }

        // LOCAL DEVELOPMENT: Use the configured connection string.
        if (storageAccount.StartsWith(
                "DefaultEndpointsProtocol=",
                StringComparison.OrdinalIgnoreCase) ||
            storageAccount.StartsWith(
                "UseDevelopmentStorage=",
                StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation(
                "Using connection-string authentication for Blob Storage.");

            return new BlobServiceClient(storageAccount);
        }

        // AZURE: Use Managed Identity with the Blob Storage endpoint.
        if (Uri.TryCreate(
                storageAccount,
                UriKind.Absolute,
                out var storageUri) &&
            storageUri.Scheme == Uri.UriSchemeHttps)
        {
            var credential = new ManagedIdentityCredential(
                new ManagedIdentityCredentialOptions());

            // Verify that Managed Identity can obtain a Storage token.
            await credential.GetTokenAsync(
                new TokenRequestContext(new[] { StorageScope }),
                cancellationToken);

            _logger.LogInformation(
                "Using Managed Identity authentication for Blob Storage.");

            return new BlobServiceClient(storageUri, credential);
        }

        throw new InvalidOperationException(
            "StorageAccount must contain either a valid Blob Storage " +
            "HTTPS URL or a valid Storage Account connection string.");
    }

    public string GetRequiredContainerName()
    {
        var containerName =
            _configuration[AppConstants.StorageAccountContainerName];

        if (string.IsNullOrWhiteSpace(containerName))
        {
            throw new InvalidOperationException(
                "StorageAccount__ContainerName configuration is missing.");
        }

        return containerName;
    }
}
