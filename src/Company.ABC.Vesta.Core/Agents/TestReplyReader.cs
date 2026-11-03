// Purpose: Reads, validates, and classifies raw AI agent replies into Usable, AgentError, or Unusable states with diagnostic problem reporting.

using System.Text;
using System.Text.Json;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;

namespace Company.ABC.Vesta.Core.Agents;

public enum TestReplyStatus
{
    Usable,
    AgentError,
    Unusable
}

public sealed record TestReplyReadResult
{
    public required TestReplyStatus Status { get; init; }
    public AgentReply? Reply { get; init; }
    public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();

    public static TestReplyReadResult Usable(AgentReply reply) =>
        new() { Status = TestReplyStatus.Usable, Reply = reply };

    public static TestReplyReadResult AgentError(AgentReply reply) =>
        new() { Status = TestReplyStatus.AgentError, Reply = reply };

    public static TestReplyReadResult Unusable(IReadOnlyList<string> problems, AgentReply? partialReply = null) =>
        new() { Status = TestReplyStatus.Unusable, Problems = problems, Reply = partialReply };
}

public static class TestReplyReader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    public static TestReplyReadResult Read(
        string rawReply,
        IReadOnlySet<string> validRowIds,
        InputLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ArgumentNullException.ThrowIfNull(validRowIds);

        var problems = new List<string>();

        if (string.IsNullOrEmpty(rawReply))
        {
            return TestReplyReadResult.Unusable(["Reply is empty or null."]);
        }

        // Rule 1: Strict raw JSON boundaries - reject leading/trailing characters and markdown fences
        if (rawReply.StartsWith("```", StringComparison.Ordinal) || rawReply.EndsWith("```", StringComparison.Ordinal))
        {
            problems.Add("Reply must be raw JSON without Markdown code fences (```).");
        }

        if (char.IsWhiteSpace(rawReply[0]) || char.IsWhiteSpace(rawReply[^1]))
        {
            problems.Add("Reply must be a single raw JSON object without leading or trailing whitespace.");
        }

        if (!rawReply.StartsWith('{') || !rawReply.EndsWith('}'))
        {
            problems.Add("Reply must contain exactly one JSON object starting with '{' and ending with '}'.");
        }

        if (problems.Count > 0)
        {
            return TestReplyReadResult.Unusable(problems);
        }

        // Rule 2: Schema validation via ContractValidator
        try
        {
            ContractValidator.Validate(ContractKind.AgentReply, rawReply);
        }
        catch (ContractViolationException ex)
        {
            if (ex.Problems is { Count: > 0 })
            {
                foreach (var problem in ex.Problems)
                {
                    problems.Add($"Schema validation error: {problem}");
                }
            }
            else
            {
                problems.Add($"Schema validation error: {ex.Message}");
            }

            return TestReplyReadResult.Unusable(problems);
        }

        // Rule 3: JSON Deserialization
        AgentReply? reply;
        try
        {
            reply = JsonSerializer.Deserialize<AgentReply>(rawReply, JsonOptions);
        }
        catch (JsonException ex)
        {
            return TestReplyReadResult.Unusable([$"Malformed JSON: {ex.Message}"]);
        }

        if (reply is null)
        {
            return TestReplyReadResult.Unusable(["Deserialized agent reply is null."]);
        }

        // Rule 4: Evaluate Status discriminator
        if (string.Equals(reply.Status, "Error", StringComparison.Ordinal))
        {
            if (reply.Error is null)
            {
                problems.Add("Agent returned status 'Error' but the 'error' object is null.");
                return TestReplyReadResult.Unusable(problems, reply);
            }

            return TestReplyReadResult.AgentError(reply);
        }

        if (!string.Equals(reply.Status, "Success", StringComparison.Ordinal))
        {
            problems.Add($"Unrecognized agent reply status: '{reply.Status}'. Expected 'Success' or 'Error'.");
            return TestReplyReadResult.Unusable(problems, reply);
        }

        if (reply.Output is null)
        {
            problems.Add("Agent returned status 'Success' but the 'output' object is null.");
            return TestReplyReadResult.Unusable(problems, reply);
        }

        // Rule 5: Test file invariants
        if (string.IsNullOrWhiteSpace(reply.Output.TestFile))
        {
            problems.Add("The output 'testFile' content must not be empty or whitespace.");
        }
        else
        {
            int testFileBytes = Encoding.UTF8.GetByteCount(reply.Output.TestFile);
            if (testFileBytes > limits.MaxTestFileBytes)
            {
                problems.Add($"Generated test file size ({testFileBytes} bytes) exceeds the allowed limit ({limits.MaxTestFileBytes} bytes).");
            }
        }

        // Rule 6: Validate RowId references in notTestable
        if (reply.Output.NotTestable is not null)
        {
            for (int i = 0; i < reply.Output.NotTestable.Count; i++)
            {
                var item = reply.Output.NotTestable[i];
                if (string.IsNullOrWhiteSpace(item.RowId))
                {
                    problems.Add($"notTestable[{i}].rowId must not be blank.");
                }
                else if (!validRowIds.Contains(item.RowId))
                {
                    problems.Add($"notTestable[{i}].rowId '{item.RowId}' does not exist in the request mapping rows.");
                }
            }
        }

        // Rule 7: Validate RowId references in findings
        if (reply.Output.Findings is not null)
        {
            for (int i = 0; i < reply.Output.Findings.Count; i++)
            {
                var item = reply.Output.Findings[i];
                if (string.IsNullOrWhiteSpace(item.RowId))
                {
                    problems.Add($"findings[{i}].rowId must not be blank.");
                }
                else if (!validRowIds.Contains(item.RowId))
                {
                    problems.Add($"findings[{i}].rowId '{item.RowId}' does not exist in the request mapping rows.");
                }
            }
        }

        return problems.Count > 0
            ? TestReplyReadResult.Unusable(problems, reply)
            : TestReplyReadResult.Usable(reply);
    }
}