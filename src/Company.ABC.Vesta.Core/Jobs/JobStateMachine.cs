// Purpose: Manages Vesta job state transitions and failure handling.

using Company.ABC.Vesta.Core.Contracts;

namespace Company.ABC.Vesta.Core.Jobs;

public static class JobStateMachine
{
    public static JobDocument Move(JobDocument job, JobState newState, DateTimeOffset at)
    {
        return job with
        {
            State = newState,
            UpdatedAt = at
        };
    }

    public static JobDocument Fail(JobDocument job, JobLastError error, DateTimeOffset at)
    {
        return job with
        {
            State = JobState.Failed,
            LastError = error,
            UpdatedAt = at
        };
    }
}