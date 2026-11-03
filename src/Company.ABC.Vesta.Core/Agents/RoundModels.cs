// Purpose: Defines data records for round-level agent call records, generated test artifacts, and round execution outcomes.

using System.Text.Json.Serialization;

namespace Company.ABC.Vesta.Core.Agents;

public enum RoundOutcome
{
    Success,
    AgentRefused,
    AgentReplyUnusable
}

public sealed record AgentCallRecord
{
    [JsonPropertyName("round")]
    public required int Round { get; init; }

    [JsonPropertyName("attempt")]
    public required int Attempt { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("durationMs")]
    public required long DurationMs { get; init; }

    [JsonPropertyName("inputTokens")]
    public int? InputTokens { get; init; }

    [JsonPropertyName("outputTokens")]
    public int? OutputTokens { get; init; }

    [JsonPropertyName("request")]
    public required AgentRequest Request { get; init; }

    [JsonPropertyName("rawReply")]
    public required string RawReply { get; init; }

    [JsonPropertyName("problems")]
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
}

public sealed record GeneratedTestArtifact
{
    [JsonPropertyName("round")]
    public required int Round { get; init; }

    [JsonPropertyName("generatedAt")]
    public required DateTimeOffset GeneratedAt { get; init; }

    [JsonPropertyName("testFile")]
    public required string TestFile { get; init; }

    [JsonPropertyName("notTestable")]
    public IReadOnlyList<AgentNotTestableRow> NotTestable { get; init; } = Array.Empty<AgentNotTestableRow>();

    [JsonPropertyName("findings")]
    public IReadOnlyList<AgentFinding> Findings { get; init; } = Array.Empty<AgentFinding>();
}

public sealed record RoundResult
{
    public required RoundOutcome Outcome { get; init; }
    public GeneratedTestArtifact? Artifact { get; init; }
    public AgentError? Error { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    public int TotalInputTokens { get; init; }
    public int TotalOutputTokens { get; init; }
}