// Purpose: Implements the Vesta Core IAgentTransport boundary using the Azure AI Foundry SDK.
// Maintains a single reused client and isolates Azure dependencies from the Core domain.

using System.Text;
using Azure.AI.Projects;
using Azure.AI.Agents.Persistent;
using Azure.Identity;
using Company.ABC.Vesta.Core.Agents;
using Company.ABC.Vesta.Foundry.Configuration;

namespace Company.ABC.Vesta.Foundry.Agents;

public sealed class FoundryAgentTransport : IAgentTransport
{
    private readonly AIProjectClient _projectClient;
    private readonly string _agentId;
    private readonly TimeSpan _defaultTimeout;

    public FoundryAgentTransport(FoundryOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.ProjectEndpoint))
        {
            throw new ArgumentException("Foundry ProjectEndpoint is required.", nameof(options));
        }

        if (string.IsNullOrWhiteSpace(options.AgentId))
        {
            throw new ArgumentException("Foundry AgentId is required.", nameof(options));
        }

        _agentId = options.AgentId;
        _defaultTimeout = TimeSpan.FromSeconds(options.TimeoutSeconds > 0 ? options.TimeoutSeconds : 600);

        _projectClient = new AIProjectClient(
            new Uri(options.ProjectEndpoint),
            new DefaultAzureCredential());
    }

    public async Task<AgentTransportResponse> SendAsync(
        string requestJson,
        CancellationToken cancellationToken)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(_defaultTimeout);
        CancellationToken token = timeoutCts.Token;

        var agentsClient = _projectClient.GetPersistentAgentsClient();

        // 1. Create a thread
        var thread = await agentsClient.Threads.CreateThreadAsync(cancellationToken: token);

        try
        {
            // 2. Add the user message (the serialized AgentRequest JSON)
            await agentsClient.Messages.CreateMessageAsync(
                thread.Value.Id,
                MessageRole.User,
                requestJson,
                cancellationToken: token);

            // 3. Start the run
            var run = await agentsClient.Runs.CreateRunAsync(
                thread.Value.Id,
                _agentId,
                cancellationToken: token);

            // 4. Poll until terminal status
            while (run.Value.Status == RunStatus.Queued ||
                   run.Value.Status == RunStatus.InProgress ||
                   run.Value.Status == RunStatus.RequiresAction)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), token);
                run = await agentsClient.Runs.GetRunAsync(thread.Value.Id, run.Value.Id, cancellationToken: token);
            }

            if (run.Value.Status != RunStatus.Completed)
            {
                throw new InvalidOperationException($"Foundry agent run ended with status: {run.Value.Status}. Error: {run.Value.LastError?.Message}");
            }

            // 5. Retrieve the agent's reply
            var agentMessages = new List<string>();

            await foreach (var msg in agentsClient.Messages.GetMessagesAsync(thread.Value.Id, cancellationToken: token))
            {
                if (msg.Role == MessageRole.Agent)
                {
                    var sb = new StringBuilder();
                    foreach (var contentItem in msg.ContentItems)
                    {
                        if (contentItem is MessageTextContent textContent)
                        {
                            sb.Append(textContent.Text);
                        }
                    }
                    agentMessages.Add(sb.ToString());
                }
            }

            if (agentMessages.Count == 0)
            {
                throw new InvalidOperationException("Foundry agent completed the run but returned no message.");
            }

            string replyText = agentMessages[0];

            int inputTokens = (int)(run.Value.Usage?.PromptTokens ?? 0L);
            int outputTokens = (int)(run.Value.Usage?.CompletionTokens ?? 0L);

            return new AgentTransportResponse(replyText, inputTokens, outputTokens);
        }
        finally
        {
            // 6. Clean up the thread
            try
            {
                await agentsClient.Threads.DeleteThreadAsync(thread.Value.Id, cancellationToken: CancellationToken.None);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }
}