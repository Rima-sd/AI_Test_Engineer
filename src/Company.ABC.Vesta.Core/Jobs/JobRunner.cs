// Purpose: Orchestrates the end-to-end Vesta pipeline for a single job step.
// Manages state transitions, input processing, agent execution, output generation, and error handling.

using Company.ABC.Vesta.Core.Agents;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Inputs;

#pragma warning disable S125 // Sections of code should not be commented out
#pragma warning disable S4487 // Unread private fields (used in upcoming pipeline steps)

namespace Company.ABC.Vesta.Core.Jobs;

public sealed class JobRunner
{
    private readonly IJobFiles _jobFiles;
    private readonly IJobStateUpdater _stateUpdater;
    private readonly IOutputStore _outputStore;
    private readonly RoundRunner _roundRunner;
    private readonly InputLimits _limits;
    private readonly TimeProvider _timeProvider;

    public JobRunner(
        IAgentTransport transport,
        IJobFiles jobFiles,
        IJobStateUpdater stateUpdater,
        IOutputStore outputStore,
        InputLimits limits,
        TimeProvider timeProvider)
    {
        _jobFiles = jobFiles;
        _stateUpdater = stateUpdater;
        _outputStore = outputStore;
        _roundRunner = new RoundRunner(transport, jobFiles, limits);
        _limits = limits;
        _timeProvider = timeProvider;
    }

    public async Task ProcessStepAsync(
        StepMessage message,
        JobDocument jobDocument,
        CancellationToken cancellationToken)
    {
        // 1. Transition state to Generating
        jobDocument = jobDocument with
        {
            State = JobState.Generating,
            UpdatedAt = _timeProvider.GetUtcNow()
        };

        await _stateUpdater.UpdateStateAsync(jobDocument, cancellationToken);

        try
        {
            // 2. Input Processing (T2) invocation

            // 3. Agent Round execution (T3)

            // 4. Write Output ZIP (T5) and Mark Done
            JobState fromState = jobDocument.State;
            jobDocument = jobDocument with
            {
                State = JobState.Done,
                UpdatedAt = _timeProvider.GetUtcNow()
            };

            await _stateUpdater.UpdateStateAsync(jobDocument, cancellationToken);
            await LogAuditAsync(jobDocument.JobId, AuditAction.StepDone, fromState, jobDocument.State, "system", cancellationToken);
        }
        catch (Exception ex)
        {
            // 5. Handle Failures gracefully
            JobState fromState = jobDocument.State;
            var lastError = new JobLastError(
                Step: "Generate",
                Code: "GenerateFailed",
                Message: ex.Message,
                Details: Array.Empty<string>(),
                At: _timeProvider.GetUtcNow());

            jobDocument = jobDocument with
            {
                State = JobState.Failed,
                LastError = lastError,
                UpdatedAt = _timeProvider.GetUtcNow()
            };

            await _stateUpdater.UpdateStateAsync(jobDocument, cancellationToken);
            await LogAuditAsync(jobDocument.JobId, AuditAction.StepFailed, fromState, jobDocument.State, "system", cancellationToken);
        }
    }

    private async Task LogAuditAsync(
        string jobId,
        AuditAction action,
        JobState? fromState,
        JobState? toState,
        string actor,
        CancellationToken token)
    {
        var entry = new AuditEntry(
            At: _timeProvider.GetUtcNow(),
            Actor: actor,
            ActorVerified: true,
            Action: action,
            FromState: fromState,
            ToState: toState,
            Details: new Dictionary<string, object?>());

        await _stateUpdater.WriteAuditAsync(jobId, entry, token);
    }
}