// Purpose: Registers the Azure AI Foundry agent transport into the dependency injection container.

using Company.ABC.Vesta.Core.Agents;
using Company.ABC.Vesta.Foundry.Agents;
using Company.ABC.Vesta.Foundry.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Company.ABC.Vesta.Foundry;

public static class FoundryServiceExtensions
{
    public static IServiceCollection AddFoundryTransport(
        this IServiceCollection services,
        FoundryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        services.AddSingleton<IAgentTransport>(
            _ => new FoundryAgentTransport(options));

        return services;
    }
}