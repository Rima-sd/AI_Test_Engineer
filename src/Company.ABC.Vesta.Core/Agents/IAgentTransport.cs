
/*
 * Purpose: Defines the transport abstraction used by Vesta Core to communicate
 * with an AI agent without depending on any cloud provider or SDK.
 */

namespace Company.ABC.Vesta.Core.Agents;

/// <summary>
/// Sends a serialized request to the agent and returns its raw reply and token usage.
/// </summary>
public interface IAgentTransport
{
    /// <summary>
    /// Sends one request to the agent.
    /// The returned reply must remain unchanged.
    /// </summary>
    Task<AgentTransportResponse> SendAsync(
        string requestJson,
        CancellationToken cancellationToken);
}
