// Purpose: Defines the message contract used to trigger Vesta processing steps.
// Each message identifies the job, requested step, processing round and requester.

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Represents a message published to the Vesta steps topic.
/// </summary>
public sealed record StepMessage(
    Guid MessageId,
    string JobId,
    VestaStep Step,
    int Round,
    string RequestedBy,
    DateTimeOffset RequestedAt);

/// <summary>
/// Identifies the processing step requested by a message.
/// </summary>
public enum VestaStep
{
    Receive,
    Generate,
    Check,
    Package
}