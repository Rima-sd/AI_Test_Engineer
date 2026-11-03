// Purpose: Orchestrates a single test generation round, managing transport calls, raw log persistence, unusable reply retries, and generated artifact saving.

using System.Diagnostics;
using System.Text.Json;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Agents;

public sealed class RoundRunner
{
    private readonly IAgentTransport _transport;
    private readonly IJobFiles _jobFiles;
    private readonly InputLimits _limits;

    public RoundRunner(
        IAgentTransport transport,
        IJobFiles jobFiles,
        InputLimits limits)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _jobFiles = jobFiles ?? throw new ArgumentNullException(nameof(jobFiles));
        _limits = limits ?? throw new ArgumentNullException(nameof(limits));
    }

    public async Task<RoundResult> RunRoundAsync(
        string jobId,
        int roundNumber,
        AgentRequest initialRequest,
        IReadOnlySet<string> validRowIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(initialRequest);
        ArgumentNullException.ThrowIfNull(validRowIds);

        string artifactPath = $"generated/v{roundNumber}.json";

        // Step 1: Crash-restart support — check if generated artifact already exists
        byte[]? existingArtifactBytes = await _jobFiles.TryReadAsync(jobId, artifactPath, cancellationToken);
        if (existingArtifactBytes is { Length: > 0 })
        {
            var existingArtifact = JsonSerializer.Deserialize<GeneratedTestArtifact>(existingArtifactBytes, ContractJson.Options);
            if (existingArtifact is not null)
            {
                return new RoundResult
                {
                    Outcome = RoundOutcome.Success,
                    Artifact = existingArtifact,
                    TotalInputTokens = 0,
                    TotalOutputTokens = 0
                };
            }
        }

        int totalInputTokens = 0;
        int totalOutputTokens = 0;

        // Step 2: Attempt 1 (k = 1)
        var call1 = await ExecuteAttemptAsync(
            jobId,
            roundNumber,
            attempt: 1,
            initialRequest,
            validRowIds,
            cancellationToken);

        totalInputTokens += call1.Response.InputTokens;
        totalOutputTokens += call1.Response.OutputTokens;

        if (call1.ReadResult.Status == TestReplyStatus.Usable)
        {
            var artifact = await SaveArtifactAsync(jobId, roundNumber, artifactPath, call1.ReadResult.Reply!.Output!, cancellationToken);
            return new RoundResult
            {
                Outcome = RoundOutcome.Success,
                Artifact = artifact,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens
            };
        }

        if (call1.ReadResult.Status == TestReplyStatus.AgentError)
        {
            return new RoundResult
            {
                Outcome = RoundOutcome.AgentRefused,
                Error = call1.ReadResult.Reply?.Error,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens
            };
        }

        // Step 3: Attempt 2 (k = 2) - Retry unusable reply with collected problems
        var retryRequest = BuildRetryRequest(initialRequest, call1.ReadResult.Problems);

        var call2 = await ExecuteAttemptAsync(
            jobId,
            roundNumber,
            attempt: 2,
            retryRequest,
            validRowIds,
            cancellationToken);

        totalInputTokens += call2.Response.InputTokens;
        totalOutputTokens += call2.Response.OutputTokens;

        if (call2.ReadResult.Status == TestReplyStatus.Usable)
        {
            var artifact = await SaveArtifactAsync(jobId, roundNumber, artifactPath, call2.ReadResult.Reply!.Output!, cancellationToken);
            return new RoundResult
            {
                Outcome = RoundOutcome.Success,
                Artifact = artifact,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens
            };
        }

        if (call2.ReadResult.Status == TestReplyStatus.AgentError)
        {
            return new RoundResult
            {
                Outcome = RoundOutcome.AgentRefused,
                Error = call2.ReadResult.Reply?.Error,
                TotalInputTokens = totalInputTokens,
                TotalOutputTokens = totalOutputTokens
            };
        }

        return new RoundResult
        {
            Outcome = RoundOutcome.AgentReplyUnusable,
            Problems = call2.ReadResult.Problems,
            TotalInputTokens = totalInputTokens,
            TotalOutputTokens = totalOutputTokens
        };
    }

    private async Task<(AgentTransportResponse Response, TestReplyReadResult ReadResult)> ExecuteAttemptAsync(
        string jobId,
        int roundNumber,
        int attempt,
        AgentRequest request,
        IReadOnlySet<string> validRowIds,
        CancellationToken cancellationToken)
    {
        string requestJson = JsonSerializer.Serialize(request, ContractJson.Options);

        var stopwatch = Stopwatch.StartNew();
        var transportResponse = await _transport.SendAsync(requestJson, cancellationToken);
        stopwatch.Stop();

        var readResult = TestReplyReader.Read(transportResponse.ReplyText, validRowIds, _limits);

        var callRecord = new AgentCallRecord
        {
            Round = roundNumber,
            Attempt = attempt,
            RequestId = request.RequestId,
            DurationMs = stopwatch.ElapsedMilliseconds,
            InputTokens = transportResponse.InputTokens,
            OutputTokens = transportResponse.OutputTokens,
            Request = request,
            RawReply = transportResponse.ReplyText,
            Problems = readResult.Problems
        };

        string callLogPath = $"agents/round-{roundNumber}-{attempt}.json";
        byte[] callLogBytes = JsonSerializer.SerializeToUtf8Bytes(callRecord, ContractJson.Options);

        await _jobFiles.TryWriteNewAsync(jobId, callLogPath, callLogBytes, cancellationToken);

        return (transportResponse, readResult);
    }

    private static AgentRequest BuildRetryRequest(AgentRequest original, IReadOnlyList<string> problems)
    {
        return new AgentRequest
        {
            Agent = original.Agent,
            RequestId = Guid.NewGuid().ToString(),
            JobId = original.JobId,
            ContractVersions = original.ContractVersions,
            Input = new AgentRequestInput
            {
                Mode = original.Input.Mode,
                TargetTestFile = original.Input.TargetTestFile,
                Package = original.Input.Package,
                GeneratedFiles = original.Input.GeneratedFiles,
                Rows = original.Input.Rows,
                TestCases = original.Input.TestCases,
                PreviousTestFile = original.Input.PreviousTestFile,
                Problems = problems
            }
        };
    }

    private async Task<GeneratedTestArtifact> SaveArtifactAsync(
        string jobId,
        int roundNumber,
        string artifactPath,
        AgentReplyOutput output,
        CancellationToken cancellationToken)
    {
        var artifact = new GeneratedTestArtifact
        {
            Round = roundNumber,
            GeneratedAt = DateTimeOffset.UtcNow,
            TestFile = output.TestFile,
            NotTestable = output.NotTestable,
            Findings = output.Findings
        };

        byte[] artifactBytes = JsonSerializer.SerializeToUtf8Bytes(artifact, ContractJson.Options);
        await _jobFiles.TryWriteNewAsync(jobId, artifactPath, artifactBytes, cancellationToken);

        return artifact;
    }
}