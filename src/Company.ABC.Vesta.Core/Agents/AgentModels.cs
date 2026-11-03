
/*
 * Purpose: Defines the strongly typed request, input, reply and transport models
 * used by Vesta Core for the agent contract version 0.1.
 */

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Company.ABC.Vesta.Core.Agents;

/// <summary>
/// Contains the raw response and token usage returned by an agent transport.
/// </summary>
public sealed record AgentTransportResponse(
    string ReplyText,
    int InputTokens,
    int OutputTokens);

/// <summary>
/// Represents a complete request sent to the Vesta agent.
/// </summary>
public sealed record AgentRequest
{
    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    [JsonPropertyName("jobId")]
    public required string JobId { get; init; }

    [JsonPropertyName("contractVersions")]
    public required AgentContractVersions ContractVersions { get; init; }

    [JsonPropertyName("input")]
    public required AgentRequestInput Input { get; init; }
}

/// <summary>
/// Contains the versions of the agent and mapping contracts.
/// </summary>
public sealed record AgentContractVersions
{
    [JsonPropertyName("agent")]
    public required string Agent { get; init; }

    [JsonPropertyName("mapping")]
    public required string Mapping { get; init; }
}

/// <summary>
/// Contains all input data required by the Vesta agent.
/// </summary>
public sealed record AgentRequestInput
{
    [JsonPropertyName("mode")]
    public required string Mode { get; init; }

    [JsonPropertyName("testFile")]
    public required AgentTestFile TargetTestFile { get; init; }

    [JsonPropertyName("package")]
    public required AgentPackage Package { get; init; }

    [JsonPropertyName("generatedFiles")]
    public required IReadOnlyList<AgentGeneratedFile> GeneratedFiles { get; init; }

    [JsonPropertyName("rows")]
    public required IReadOnlyList<AgentMappingRow> Rows { get; init; }

    [JsonPropertyName("testCases")]
    public required IReadOnlyList<AgentTestCase> TestCases { get; init; }

    [JsonPropertyName("previousTestFile")]
    public string? PreviousTestFile { get; init; }

    [JsonPropertyName("problems")]
    public required IReadOnlyList<string> Problems { get; init; }
}

/// <summary>
/// Identifies the test file that the agent must generate or repair.
/// </summary>
public sealed record AgentTestFile
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("namespace")]
    public required string Namespace { get; init; }

    [JsonPropertyName("className")]
    public required string ClassName { get; init; }
}

/// <summary>
/// Contains the package file names supplied to the agent.
/// </summary>
public sealed record AgentPackage
{
    [JsonPropertyName("Files")]
    public required IReadOnlyList<string> Files { get; init; }
}

/// <summary>
/// Represents one generated source or mapping file.
/// </summary>
public sealed record AgentGeneratedFile
{
    [JsonPropertyName("path")]
    public required string Path { get; init; }

    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>
/// Represents one mapping row that must be covered by generated tests.
/// </summary>
public sealed record AgentMappingRow
{
    [JsonPropertyName("rowId")]
    public required string RowId { get; init; }

    [JsonPropertyName("scopeId")]
    public required string ScopeId { get; init; }

    [JsonPropertyName("targetPath")]
    public required string TargetPath { get; init; }

    [JsonPropertyName("ruleType")]
    public required string RuleType { get; init; }
}

/// <summary>
/// Represents one business-approved test case.
/// Input and expected values can contain any valid JSON value.
/// </summary>
public sealed record AgentTestCase
{
    [JsonPropertyName("rowId")]
    public required string RowId { get; init; }

    [JsonPropertyName("targetPath")]
    public required string TargetPath { get; init; }

    [JsonPropertyName("input")]
    public required JsonElement Input { get; init; }

    [JsonPropertyName("expected")]
    public required JsonElement Expected { get; init; }
}

/// <summary>
/// Represents the complete response returned by the Vesta agent.
/// </summary>
public sealed record AgentReply
{
    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("output")]
    public AgentReplyOutput? Output { get; init; }

    [JsonPropertyName("error")]
    public AgentError? Error { get; init; }
}

/// <summary>
/// Contains the generated test file and agent observations.
/// </summary>
public sealed record AgentReplyOutput
{
    [JsonPropertyName("testFile")]
    public required string TestFile { get; init; }

    [JsonPropertyName("notTestable")]
    public required IReadOnlyList<AgentNotTestableRow> NotTestable { get; init; }

    [JsonPropertyName("findings")]
    public required IReadOnlyList<AgentFinding> Findings { get; init; }
}

/// <summary>
/// Explains why a mapping row cannot be tested.
/// </summary>
public sealed record AgentNotTestableRow
{
    [JsonPropertyName("rowId")]
    public required string RowId { get; init; }

    [JsonPropertyName("reason")]
    public required string Reason { get; init; }
}

/// <summary>
/// Represents a finding reported by the agent about the mapping and generated code.
/// </summary>
public sealed record AgentFinding
{
    [JsonPropertyName("rowId")]
    public required string RowId { get; init; }

    [JsonPropertyName("note")]
    public required string Note { get; init; }
}

/// <summary>
/// Represents an error returned by the agent.
/// </summary>
public sealed record AgentError
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("message")]
    public required string Message { get; init; }

    [JsonPropertyName("details")]
    public required IReadOnlyList<string> Details { get; init; }
}
