
// Purpose: Defines source-generated structured logging events used throughout Vesta.
// Logs identifiers, states, counts, sizes, durations and error codes only.
// Never log message bodies, file contents, prompts, replies, tokens or HTTP headers.

using Microsoft.Extensions.Logging;

namespace Company.ABC.Vesta.Functions.Logging;

internal static partial class Log
{
    [LoggerMessage(
        EventId = 1001,
        Level = LogLevel.Information,
        Message = "Vesta job created. JobId: {JobId}, State: {State}")]
    internal static partial void JobCreated(
        ILogger logger,
        string jobId,
        string state);

    [LoggerMessage(
        EventId = 1002,
        Level = LogLevel.Information,
        Message = "Vesta job state changed. JobId: {JobId}, FromState: {FromState}, ToState: {ToState}")]
    internal static partial void JobStateChanged(
        ILogger logger,
        string jobId,
        string fromState,
        string toState);

    [LoggerMessage(
        EventId = 1003,
        Level = LogLevel.Information,
        Message = "Vesta step message published. JobId: {JobId}, Step: {Step}, Round: {Round}")]
    internal static partial void StepMessagePublished(
        ILogger logger,
        string jobId,
        string step,
        int round);

    [LoggerMessage(
        EventId = 1004,
        Level = LogLevel.Information,
        Message = "Vesta step completed. JobId: {JobId}, Step: {Step}, Round: {Round}, DurationMs: {DurationMs}")]
    internal static partial void StepCompleted(
        ILogger logger,
        string jobId,
        string step,
        int round,
        long durationMs);

    [LoggerMessage(
        EventId = 1005,
        Level = LogLevel.Warning,
        Message = "Vesta step failed. JobId: {JobId}, Step: {Step}, Round: {Round}, ErrorCode: {ErrorCode}")]
    internal static partial void StepFailed(
        ILogger logger,
        string jobId,
        string step,
        int round,
        string errorCode);

    [LoggerMessage(
        EventId = 1006,
        Level = LogLevel.Information,
        Message = "Vesta agent call completed. JobId: {JobId}, Round: {Round}, InputTokens: {InputTokens}, OutputTokens: {OutputTokens}, DurationMs: {DurationMs}")]
    internal static partial void AgentCallCompleted(
        ILogger logger,
        string jobId,
        int round,
        int inputTokens,
        int outputTokens,
        long durationMs);

    [LoggerMessage(
        EventId = 1007,
        Level = LogLevel.Information,
        Message = "Vesta validation completed. JobId: {JobId}, Round: {Round}, Passed: {Passed}, ProblemCount: {ProblemCount}, WarningCount: {WarningCount}")]
    internal static partial void ValidationCompleted(
        ILogger logger,
        string jobId,
        int round,
        bool passed,
        int problemCount,
        int warningCount);

    [LoggerMessage(
        EventId = 1008,
        Level = LogLevel.Information,
        Message = "Vesta ZIP processed. JobId: {JobId}, EntryCount: {EntryCount}, CompressedBytes: {CompressedBytes}, UnpackedBytes: {UnpackedBytes}")]
    internal static partial void ZipProcessed(
        ILogger logger,
        string jobId,
        int entryCount,
        long compressedBytes,
        long unpackedBytes);

    [LoggerMessage(
        EventId = 1009,
        Level = LogLevel.Information,
        Message = "Vesta dependency check completed. Dependency: {Dependency}, Exists: {Exists}, CanRead: {CanRead}, CanWrite: {CanWrite}")]
    internal static partial void DependencyCheckCompleted(
        ILogger logger,
        string dependency,
        bool exists,
        bool canRead,
        bool? canWrite);

    [LoggerMessage(
        EventId = 1010,
        Level = LogLevel.Warning,
        Message = "Vesta dependency check failed. Dependency: {Dependency}, StatusCode: {StatusCode}")]
    internal static partial void DependencyCheckFailed(
        ILogger logger,
        string dependency,
        int statusCode);
}
