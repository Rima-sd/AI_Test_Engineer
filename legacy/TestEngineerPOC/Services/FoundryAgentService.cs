#pragma warning disable OPENAI001

using Azure.AI.Projects;
using Azure.AI.Extensions.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using OpenAI.Responses;
using System.Diagnostics;
using Azure.Core;

namespace TestEngineerPOC.Services;

public sealed class FoundryAgentService
{
    private readonly ProjectResponsesClient _responsesClient;
    private readonly ILogger<FoundryAgentService> _logger;

    public FoundryAgentService(
        ILogger<FoundryAgentService> logger)
    {
        _logger = logger;

        string endpoint =
            Environment.GetEnvironmentVariable("Foundry__ProjectEndpoint")
            ?? throw new InvalidOperationException(
                "Foundry__ProjectEndpoint is not configured.");

        string agentName =
            Environment.GetEnvironmentVariable("Foundry__AgentName")
            ?? throw new InvalidOperationException(
                "Foundry__AgentName is not configured.");

        _logger.LogInformation(
            "Initializing Foundry agent client. AgentName: {AgentName}",
            agentName);

        var credential = new ManagedIdentityCredential(ManagedIdentityId.SystemAssigned);

        var token = credential.GetToken(
            new TokenRequestContext(
                new[] { "https://ai.azure.com/.default" }));

        var tokenParts = token.Token.Split('.');

        if (tokenParts.Length >= 2)
        {
            var payload = tokenParts[1]
                .Replace('-', '+')
                .Replace('_', '/');

            payload = payload.PadRight(
                payload.Length + (4 - payload.Length % 4) % 4,
                '=');

            var payloadJson = System.Text.Encoding.UTF8.GetString(
                Convert.FromBase64String(payload));

            using var document = System.Text.Json.JsonDocument.Parse(payloadJson);
            var root = document.RootElement;

            _logger.LogInformation(
                "Foundry token claims. Audience: {Audience}, TenantId: {TenantId}, ObjectId: {ObjectId}, AppId: {AppId}",
                root.TryGetProperty("aud", out var aud) ? aud.GetString() : "missing",
                root.TryGetProperty("tid", out var tid) ? tid.GetString() : "missing",
                root.TryGetProperty("oid", out var oid) ? oid.GetString() : "missing",
                root.TryGetProperty("appid", out var appid) ? appid.GetString() : "missing");
        }

        _logger.LogInformation(
            "Foundry managed identity token acquired. ExpiresOn: {ExpiresOn}",
            token.ExpiresOn);

        var projectClient = new AIProjectClient(new Uri(endpoint), credential);


        _responsesClient = projectClient.ProjectOpenAIClient
                .GetProjectResponsesClientForAgent(
    new Azure.AI.Extensions.OpenAI.AgentReference(
        agentName,
        Environment.GetEnvironmentVariable("Foundry__AgentVersion")!));

        _logger.LogInformation(
            "Foundry agent client initialized. AgentName: {AgentName}",
            agentName);
    }

    public async Task<string> RunAsync(
        string requestId,
        string prompt,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        _logger.LogInformation(
            "Foundry agent request started. RequestId: {RequestId}",
            requestId);

        try
        {
            var options = new CreateResponseOptions
            {
                InputItems =
                {
                    ResponseItem.CreateUserMessageItem(prompt)
                }
            };

            var clientResult = await _responsesClient.CreateResponseAsync(options, cancellationToken);

            var response = clientResult.Value;
            string result = response.GetOutputText();

            stopwatch.Stop();

            _logger.LogInformation(
                "Foundry agent request completed. RequestId: {RequestId}, DurationMs: {DurationMs}, ResponseLength: {ResponseLength}",
                requestId,
                stopwatch.ElapsedMilliseconds,
                result.Length);

            if (string.IsNullOrWhiteSpace(result))
            {
                throw new InvalidOperationException(
                    "Foundry agent returned an empty response.");
            }

            return result;
        }

        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(
                ex,
                "Foundry request failed. RequestId: {RequestId}, " +
                "ExceptionType: {ExceptionType}, Message: {Message}, " +
                "DurationMs: {DurationMs}",
                requestId,
                ex.GetType().FullName,
                ex.Message,
                stopwatch.ElapsedMilliseconds);

            if (ex is System.ClientModel.ClientResultException clientEx)
            {
                var rawResponse = clientEx.GetRawResponse();

                _logger.LogError(
                    "Foundry HTTP error details. RequestId: {RequestId}, " +
                    "Status: {Status}, ResponseBody: {ResponseBody}",
                    requestId,
                    clientEx.Status,
                    rawResponse?.Content.ToString() ?? "No response body available");

                _logger.LogError(
                    "Foundry response metadata. RequestId: {RequestId}, " +
                    "Status: {Status}, ReasonPhrase: {ReasonPhrase}, " +
                    "ResponseHeaders: {ResponseHeaders}",
                    requestId,
                    rawResponse?.Status,
                    rawResponse?.ReasonPhrase,
                   rawResponse?.Headers
                    .Select(h => $"{h.Key}={string.Join(",", h.Value)}")
                    .Aggregate((a, b) => $"{a}; {b}")
                    ?? "No headers available");
            }

            throw;
        }


    }
}

#pragma warning restore OPENAI001
