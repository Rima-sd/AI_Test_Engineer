// Purpose: Exercises the 10 T3 agent workflow scenarios against RoundRunner using ScriptedTransport and in-memory storage.

using System.Text.Json;
using Company.ABC.Vesta.Core.Agents;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;

namespace Company.ABC.Vesta.Inspect;

internal static class AgentSamplesCheck
{
    private const string JobId = "price-tiers-code-20261030-01-t01";
    private static readonly HashSet<string> ValidRowIds = ["R001", "R002", "R005"];

    private static readonly InputLimits Limits = new(
        maxZipBytes: 5_000_000,
        maxZipEntries: 100,
        maxZipUnpackedBytes: 5_000_000,
        maxAgentInputBytes: 1_000_000,
        maxTestFileBytes: 262_144);

    public static async Task<int> RunAsync()
    {
        Console.WriteLine("Vesta T3 agent sample checks");
        int failures = 0;

        failures += await CheckValidReplyFirstCall();
        failures += await CheckFencedReplyThenValid();
        failures += await CheckNonJsonReplyThenValid();
        failures += await CheckExtraPropertyThenValid();
        failures += await CheckEmptyTestFileTwice();
        failures += await CheckUnknownRowIdThenValid();
        failures += await CheckAgentErrorRefusal();
        failures += await CheckRepairRoundValid();
        failures += await CheckRetryContainsProblems();
        failures += await CheckRerunWhenArtifactExistsMakesNoTransportCall();

        Console.WriteLine();
        Console.WriteLine($"Agent sample checks completed: {failures} failure(s).");
        return failures == 0 ? 0 : 1;
    }

    // 1. Valid reply on first call
    private static async Task<int> CheckValidReplyFirstCall()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue(BuildSuccessReply("// Valid tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 1
                      && jobFiles.Files.ContainsKey($"{JobId}/generated/v1.json")
                      && jobFiles.Files.ContainsKey($"{JobId}/agents/round-1-1.json");

        return Report("Valid reply on first call", passed);
    }

    // 2. Fenced reply, then valid reply
    private static async Task<int> CheckFencedReplyThenValid()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue("```json\n" + BuildSuccessReply("// Valid tests") + "\n```");
        transport.Enqueue(BuildSuccessReply("// Valid tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 2
                      && jobFiles.Files.ContainsKey($"{JobId}/agents/round-1-1.json")
                      && jobFiles.Files.ContainsKey($"{JobId}/agents/round-1-2.json")
                      && jobFiles.Files.ContainsKey($"{JobId}/generated/v1.json");

        return Report("Fenced reply, then valid reply", passed);
    }

    // 3. Non-JSON reply, then valid reply
    private static async Task<int> CheckNonJsonReplyThenValid()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue("Here is your test file:\npublic class Foo {}");
        transport.Enqueue(BuildSuccessReply("// Valid tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 2;

        return Report("Non-JSON reply, then valid reply", passed);
    }

    // 4. Extra JSON property, then valid reply
    private static async Task<int> CheckExtraPropertyThenValid()
    {
        var transport = new ScriptedTransport();
        string extraPropReply = """
        {
            "status": "Success",
            "output": {
                "testFile": "// Valid tests",
                "notTestable": [],
                "findings": [],
                "unauthorizedExtraProperty": 123
            },
            "error": null
        }
        """;
        transport.Enqueue(extraPropReply);
        transport.Enqueue(BuildSuccessReply("// Valid tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 2;

        return Report("Extra JSON property, then valid reply", passed);
    }

    // 5. Empty testFile twice (unusable)
    private static async Task<int> CheckEmptyTestFileTwice()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue(BuildSuccessReply("   "));
        transport.Enqueue(BuildSuccessReply(""));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.AgentReplyUnusable
                      && transport.ReceivedRequests.Count == 2
                      && !jobFiles.Files.ContainsKey($"{JobId}/generated/v1.json");

        return Report("Empty testFile twice (unusable)", passed);
    }

    // 6. Unknown rowId in notTestable, then valid reply
    private static async Task<int> CheckUnknownRowIdThenValid()
    {
        var transport = new ScriptedTransport();
        string unknownRowReply = """
        {
            "status": "Success",
            "output": {
                "testFile": "// Valid tests",
                "notTestable": [
                    { "rowId": "R999", "reason": "Unknown row" }
                ],
                "findings": []
            },
            "error": null
        }
        """;
        transport.Enqueue(unknownRowReply);
        transport.Enqueue(BuildSuccessReply("// Valid tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 2;

        return Report("Unknown rowId in notTestable, then valid reply", passed);
    }

    // 7. Agent error InvalidInput (refused)
    private static async Task<int> CheckAgentErrorRefusal()
    {
        var transport = new ScriptedTransport();
        string errorReply = """
        {
            "status": "Error",
            "output": null,
            "error": {
                "code": "InvalidInput",
                "message": "Input was invalid",
                "details": ["Missing mapping details"]
            }
        }
        """;
        transport.Enqueue(errorReply);

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.AgentRefused
                      && transport.ReceivedRequests.Count == 1
                      && result.Error?.Code == "InvalidInput";

        return Report("Agent error InvalidInput (refused)", passed);
    }

    // 8. Repair round returning a valid reply
    private static async Task<int> CheckRepairRoundValid()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue(BuildSuccessReply("// Repaired tests"));

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        var result = await runner.RunRoundAsync(JobId, 2, CreateSampleRequest("Repair"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 1
                      && jobFiles.Files.ContainsKey($"{JobId}/generated/v2.json");

        return Report("Repair round returning a valid reply", passed);
    }

    // 9. Retry request contains the previous problems
    private static async Task<int> CheckRetryContainsProblems()
    {
        var transport = new ScriptedTransport();
        transport.Enqueue(BuildSuccessReply("   ")); // Call 1: empty testFile
        transport.Enqueue(BuildSuccessReply("// Valid tests")); // Call 2: valid

        var jobFiles = new InMemoryJobFiles();
        var runner = new RoundRunner(transport, jobFiles, Limits);

        await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool retryHasProblems = false;
        if (transport.ReceivedRequests.Count == 2)
        {
            var call2Req = JsonSerializer.Deserialize<AgentRequest>(transport.ReceivedRequests[1], ContractJson.Options);
            retryHasProblems = call2Req?.Input.Problems.Count > 0
                               && call2Req.Input.Problems.Any(p => p.Contains("empty or whitespace", StringComparison.OrdinalIgnoreCase));
        }

        return Report("Retry request contains the previous problems", retryHasProblems);
    }

    // 10. Rerunning when generated/v1.json exists makes no transport call
    private static async Task<int> CheckRerunWhenArtifactExistsMakesNoTransportCall()
    {
        var transport = new ScriptedTransport();
        var jobFiles = new InMemoryJobFiles();

        var existingArtifact = new GeneratedTestArtifact
        {
            Round = 1,
            GeneratedAt = DateTimeOffset.UtcNow,
            TestFile = "// Pre-existing tests",
            NotTestable = [],
            Findings = []
        };
        byte[] artifactBytes = JsonSerializer.SerializeToUtf8Bytes(existingArtifact, ContractJson.Options);
        await jobFiles.TryWriteNewAsync(JobId, "generated/v1.json", artifactBytes);

        var runner = new RoundRunner(transport, jobFiles, Limits);
        var result = await runner.RunRoundAsync(JobId, 1, CreateSampleRequest("Initial"), ValidRowIds);

        bool passed = result.Outcome == RoundOutcome.Success
                      && transport.ReceivedRequests.Count == 0
                      && result.Artifact?.TestFile == "// Pre-existing tests";

        return Report("Rerunning when generated/v1.json exists makes no transport call", passed);
    }

    private static AgentRequest CreateSampleRequest(string mode)
    {
        return new AgentRequest
        {
            Agent = "vesta",
            RequestId = Guid.NewGuid().ToString(),
            JobId = JobId,
            ContractVersions = new AgentContractVersions
            {
                Agent = "0.1",
                Mapping = "0.1"
            },
            Input = new AgentRequestInput
            {
                Mode = mode,
                TargetTestFile = new AgentTestFile
                {
                    Path = "Tests/Transformation/MappingTests.cs",
                    Namespace = "Company.ABC.Tests.Transformation",
                    ClassName = "MappingTests"
                },
                Package = new AgentPackage
                {
                    Files = ["source.json", "target.json", "mapping.json"]
                },
                GeneratedFiles =
                [
                    new AgentGeneratedFile { Path = "Models/InputDTO.cs", Content = "// input" },
                    new AgentGeneratedFile { Path = "Models/OutputDTO.cs", Content = "// output" },
                    new AgentGeneratedFile { Path = "Transformation/Mapping.cs", Content = "// mapping" },
                    new AgentGeneratedFile { Path = "Transformation/MappingLogic.md", Content = "// logic" }
                ],
                Rows =
                [
                    new AgentMappingRow { RowId = "R001", ScopeId = "S001", TargetPath = "A", RuleType = "Copy" },
                    new AgentMappingRow { RowId = "R002", ScopeId = "S001", TargetPath = "B", RuleType = "Copy" },
                    new AgentMappingRow { RowId = "R005", ScopeId = "S001", TargetPath = "C", RuleType = "Copy" }
                ],
                TestCases = [],
                PreviousTestFile = null,
                Problems = []
            }
        };
    }

    private static string BuildSuccessReply(string testFile)
    {
        return JsonSerializer.Serialize(new
        {
            status = "Success",
            output = new
            {
                testFile,
                notTestable = Array.Empty<object>(),
                findings = Array.Empty<object>()
            },
            error = (object?)null
        });
    }

    private static int Report(string name, bool passed)
    {
        Console.WriteLine($"{(passed ? "PASS" : "FAIL")}: {name}");
        return passed ? 0 : 1;
    }
}