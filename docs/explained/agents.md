# Vesta Agent Orchestration

Vesta uses an isolated, highly robust orchestration pipeline to interact with AI agents. The design enforces a strict boundary between the core domain logic and external cloud SDKs, ensuring that Vesta remains testable, resilient, and vendor-agnostic at its core.

## The `IAgentTransport` Boundary

All communication with the AI agent goes through the `IAgentTransport` interface, defined in `Company.ABC.Vesta.Core.Agents`. 

```csharp
public interface IAgentTransport
{
    Task<AgentTransportResponse> SendAsync(string requestJson, CancellationToken cancellationToken);
}