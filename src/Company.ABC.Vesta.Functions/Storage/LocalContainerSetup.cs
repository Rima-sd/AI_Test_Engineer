
// Purpose: Creates only Vesta-owned containers when explicitly enabled for local development.
// Never creates or modifies Vulcan or Minerva containers.

using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Storage;

public sealed class LocalContainerSetup(
    BlobServiceClients clients,
    VestaContainers containers,
    IOptions<VestaOptions> options,
    IHostEnvironment environment) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.CreateContainersIfMissing ||
            !environment.IsDevelopment())
        {
            return;
        }

        await clients.Vesta
            .GetBlobContainerClient(containers.Jobs)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        await clients.Vesta
            .GetBlobContainerClient(containers.Output)
            .CreateIfNotExistsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
