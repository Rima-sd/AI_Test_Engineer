using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Inspect;

public sealed class InMemoryJobStateUpdater : IJobStateUpdater
{
    public JobDocument? LastJobState { get; private set; }
    public List<AuditEntry> AuditLogs { get; } = new();

    public Task UpdateStateAsync(JobDocument jobDocument, CancellationToken cancellationToken)
    {
        LastJobState = jobDocument;
        return Task.CompletedTask;
    }

    public Task WriteAuditAsync(string jobId, AuditEntry entry, CancellationToken cancellationToken)
    {
        AuditLogs.Add(entry);
        return Task.CompletedTask;
    }
}