// Purpose: Provides an in-memory, queue-based IAgentTransport implementation for testing agent round workflows without Azure.

using Company.ABC.Vesta.Core.Agents;

namespace Company.ABC.Vesta.Inspect;

internal sealed class ScriptedTransport : IAgentTransport
{
    private readonly Queue<AgentTransportResponse> _responses = new();
    private readonly List<string> _receivedRequests = [];

    public IReadOnlyList<string> ReceivedRequests => _receivedRequests;

    public void Enqueue(string replyText, int inputTokens = 100, int outputTokens = 50)
    {
        _responses.Enqueue(new AgentTransportResponse(replyText, inputTokens, outputTokens));
    }

    public Task<AgentTransportResponse> SendAsync(string requestJson, CancellationToken cancellationToken)
    {
        _receivedRequests.Add(requestJson);

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("ScriptedTransport has no more queued responses.");
        }

        return Task.FromResult(_responses.Dequeue());
    }
}