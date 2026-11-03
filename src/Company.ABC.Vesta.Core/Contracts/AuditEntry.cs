// Purpose: Defines the audit entry contract for recording Vesta job activities.
// Audit entries capture who performed an action, when it occurred and the related state transition.

using System.Text.Json.Serialization;

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Represents one append-only audit entry for a Vesta job.
/// </summary>
public sealed record AuditEntry(
    DateTimeOffset At,
    string Actor,
    bool ActorVerified,
    AuditAction Action,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JobState? FromState,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    JobState? ToState,
    IReadOnlyDictionary<string, object?> Details);

/// <summary>
/// Identifies an auditable action performed during job processing.
/// </summary>
public enum AuditAction
{
    JobCreated,
    StepDone,
    StepFailed,
    Retried,
    Cancelled
}