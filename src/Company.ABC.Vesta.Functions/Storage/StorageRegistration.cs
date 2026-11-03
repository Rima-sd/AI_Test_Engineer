// Purpose: Registers Blob service clients for Vesta, Vulcan and Minerva storage.
// Uses local connection strings when configured; otherwise uses DefaultAzureCredential.

using Azure.Identity;
using Azure.Storage.Blobs;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Storage;

public static class StorageRegistration
{
    public static IServiceCollection AddVestaStorage(
        this IServiceCollection services)
    {
        services.AddSingleton<VestaContainers>();

        services.AddSingleton(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<VestaOptions>>()
                .Value;

            var vesta = CreateVestaClient(options);
            var vulcan = CreateUpstreamClient(
                options.VulcanBlobServiceUri,
                vesta);

            var minerva = CreateUpstreamClient(
                options.MinervaBlobServiceUri,
                vesta);

            return new BlobServiceClients(vesta, vulcan, minerva);
        });

        // Register the IJobStateUpdater
        services.AddSingleton<IJobStateUpdater, BlobJobStateUpdater>();

        services.AddHostedService<LocalContainerSetup>();

        return services;
    }

    private static BlobServiceClient CreateVestaClient(
        VestaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            return new BlobServiceClient(options.ConnectionString);
        }

        return CreateUriClient(options.BlobServiceUri);
    }

    private static BlobServiceClient CreateUpstreamClient(
        string? serviceUri,
        BlobServiceClient vestaClient)
    {
        return string.IsNullOrWhiteSpace(serviceUri)
            ? vestaClient
            : CreateUriClient(serviceUri);
    }

    private static BlobServiceClient CreateUriClient(string? serviceUri)
    {
        if (!Uri.TryCreate(
                serviceUri,
                UriKind.Absolute,
                out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "A valid HTTPS Blob service URI is required.");
        }

        return new BlobServiceClient(
            uri,
            new DefaultAzureCredential());
    }
}