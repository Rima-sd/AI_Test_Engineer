// Purpose: Coordinates job state updates, audit logging, and step message publishing.

using Company.ABC.Vesta.Core.Contracts;

namespace Company.ABC.Vesta.Core.Jobs;

public sealed class JobJournal
{
    private readonly IJobStore _jobStore;
    private readonly IAuditTrail _auditTrail;
    private readonly IStepSender _stepSender;
    private readonly TimeProvider _timeProvider;

    public JobJournal(
        IJobStore jobStore,
        IAuditTrail auditTrail,
        IStepSender stepSender,
        TimeProvider timeProvider)
    {
        _jobStore = jobStore;
        _auditTrail = auditTrail;
        _stepSender = stepSender;
        _timeProvider = timeProvider;
    }

    public async Task CommitStepAsync(
        JobDocument job,
        string eTag,
        AuditAction action,
        JobState? fromState,
        JobState? toState,
        string actor,
        VestaStep? nextStepToPublish,
        int nextRound,
        CancellationToken cancellationToken)
    {
        // 1. Save job document with ETag check
        await _jobStore.ReplaceAsync(job, eTag, cancellationToken);

        // 2. Append audit entry
        var auditEntry = new AuditEntry(
            At: _timeProvider.GetUtcNow(),
            Actor: actor,
            ActorVerified: actor == "system",
            Action: action,
            FromState: fromState,
            ToState: toState,
            Details: new Dictionary<string, object?> { ["jobId"] = job.JobId });

        await _auditTrail.AppendAsync(job.JobId, auditEntry, cancellationToken);

        // 3. Publish next step message if applicable
        if (nextStepToPublish.HasValue)
        {
            var message = new StepMessage(
                MessageId: Guid.NewGuid(),
                JobId: job.JobId,
                Step: nextStepToPublish.Value,
                Round: nextRound,
                RequestedBy: actor,
                RequestedAt: _timeProvider.GetUtcNow());

            await _stepSender.PublishAsync(message, cancellationToken);
        }
    }
}