// Purpose: Defines the boundary for updating job state and appending immutable audit logs.

using Company.ABC.Vesta.Core.Contracts;

namespace Company.ABC.Vesta.Core.Jobs;

public interface IJobStateUpdater
{
    Task UpdateStateAsync(
        JobDocument jobDocument,
        CancellationToken cancellationToken);

    Task WriteAuditAsync(
        string jobId,
        AuditEntry entry,
        CancellationToken cancellationToken);
}