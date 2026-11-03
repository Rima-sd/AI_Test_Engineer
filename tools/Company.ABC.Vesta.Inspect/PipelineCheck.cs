// Purpose: Verifies end-to-end pipeline execution locally using in-memory stubs.

using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Inspect;

internal sealed class InMemoryOutputStore : IOutputStore
{
    public string? LastWrittenJobId { get; private set; }
    public ReadOnlyMemory<byte> LastWrittenContent { get; private set; }

    public Task<string> WriteNewZipAsync(
        string jobId,
        string fullName,
        ReadOnlyMemory<byte> zipContent,
        CancellationToken cancellationToken = default)
    {
        LastWrittenJobId = jobId;
        LastWrittenContent = zipContent;
        return Task.FromResult(JobPaths.OutputZip(jobId, 1));
    }
}

public static class PipelineCheck
{
    public static async Task<int> RunAsync()
    {
        Console.WriteLine("Pipeline validation started.");

        var limits = new InputLimits(50_000_000, 2000, 250_000_000, 2_000_000, 5_000_000);
        var timeProvider = TimeProvider.System;

        var transport = new ScriptedTransport();
        var files = new InMemoryJobFiles();
        var updater = new InMemoryJobStateUpdater();
        var outputStore = new InMemoryOutputStore();

        var runner = new JobRunner(transport, files, updater, outputStore, limits, timeProvider);

        var jobId = "price-tiers-code-20261102-01-t01";

        var initialJob = new JobDocument(
            JobId: jobId,
            VulcanJobId: "price-tiers-code-20261102-01",
            MappingId: "mapping-001",
            CreatedBy: "system",
            CreatedAt: timeProvider.GetUtcNow(),
            Origin: new JobOrigin(1),
            Project: new JobProject("Company.ABC.PriceTiers"),
            State: JobState.Received,
            Round: 0,
            RepairRoundsUsed: 0,
            Inputs: null,
            Check: null,
            Usage: new JobUsage(0, 0, 0),
            Output: null,
            LastError: null,
            UpdatedAt: timeProvider.GetUtcNow());

        var message = new StepMessage(
            MessageId: Guid.NewGuid(),
            JobId: jobId,
            Step: VestaStep.Generate,
            Round: 1,
            RequestedBy: "system",
            RequestedAt: timeProvider.GetUtcNow());

        Console.WriteLine("Executing JobRunner...");

        await runner.ProcessStepAsync(message, initialJob, CancellationToken.None);

        if (updater.LastJobState is null)
        {
            Console.WriteLine("FAIL: JobState was never updated.");
            return 1;
        }

        if (updater.LastJobState.State != JobState.Done)
        {
            Console.WriteLine($"FAIL: Expected JobState 'Done', got '{updater.LastJobState.State}'.");
            return 1;
        }

        if (updater.AuditLogs.Count != 1)
        {
            Console.WriteLine($"FAIL: Expected 1 audit log, got {updater.AuditLogs.Count}.");
            return 1;
        }

        if (updater.AuditLogs[0].Action != AuditAction.StepDone)
        {
            Console.WriteLine($"FAIL: Expected final audit log to be 'StepDone', got '{updater.AuditLogs[0].Action}'.");
            return 1;
        }

        Console.WriteLine("PASS: Job transitioned to Done successfully.");
        Console.WriteLine("PASS: Audit trail appended correctly.");
        Console.WriteLine("Pipeline validation completed successfully.");
        return 0;
    }
}