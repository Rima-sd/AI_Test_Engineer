// Purpose: Defines the Vesta job document and its nested data records.
// This document represents the persisted state and processing information
// for a single Vesta test-generation job.

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Represents the persisted state of a Vesta job.
/// </summary>
public sealed record JobDocument(
    string JobId,
    string VulcanJobId,
    string MappingId,
    string CreatedBy,
    DateTimeOffset CreatedAt,
    JobOrigin Origin,
    JobProject Project,
    JobState State,
    int Round,
    int RepairRoundsUsed,
    JobInputs? Inputs,
    JobCheck? Check,
    JobUsage Usage,
    JobOutput? Output,
    JobLastError? LastError,
    DateTimeOffset UpdatedAt);

/// <summary>
/// Identifies the Vulcan generation used by this job.
/// </summary>
public sealed record JobOrigin(
    int VulcanGeneration);

/// <summary>
/// Identifies the source project.
/// </summary>
public sealed record JobProject(
    string FullName);

/// <summary>
/// Describes the input inventory processed by Vesta.
/// </summary>
public sealed record JobInputs(
    bool TestCasesFound,
    int RowCount,
    int TestCaseCount);

/// <summary>
/// Records the result of a validation round.
/// </summary>
public sealed record JobCheck(
    int Round,
    bool Passed,
    bool Safe,
    int ProblemCount,
    int WarningCount);

/// <summary>
/// Tracks Azure AI Foundry usage for the job.
/// </summary>
public sealed record JobUsage(
    int AgentCalls,
    long InputTokens,
    long OutputTokens);

/// <summary>
/// Describes the packaged output.
/// </summary>
public sealed record JobOutput(
    string ZipPath,
    int Round,
    bool TestsClean);

/// <summary>
/// Records the last processing error.
/// </summary>
public sealed record JobLastError(
    string Step,
    string Code,
    string Message,
    IReadOnlyList<string> Details,
    DateTimeOffset At);

/// <summary>
/// Represents the current state of a Vesta job.
/// </summary>
public enum JobState
{
    Received,
    Generating,
    Checking,
    Packaging,
    Done,
    Failed,
    Cancelled
}