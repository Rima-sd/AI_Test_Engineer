// Purpose: Defines the configuration required to connect to Azure AI Foundry and invoke the Vesta agent.

namespace Company.ABC.Vesta.Foundry.Configuration;

public sealed record FoundryOptions
{
    public required string ProjectEndpoint { get; init; }

    // The Foundry Agent ID (or name) configured for Vesta.
    public required string AgentId { get; init; }

    public int TimeoutSeconds { get; init; } = 600;
}