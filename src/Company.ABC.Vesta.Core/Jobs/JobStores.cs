
// Purpose: Defines storage, audit, messaging and external-source interfaces for Vesta.
// Keeps Core independent of Azure SDK implementations while enforcing the planned storage contracts.

using Company.ABC.Vesta.Core.Contracts;

namespace Company.ABC.Vesta.Core.Jobs;

/// <summary>
/// Represents a job document together with its storage ETag.
/// The ETag is required for safe updates.
/// </summary>
public sealed record StoredJob(
    JobDocument Job,
    string ETag);

/// <summary>
/// Provides conditional storage operations for Vesta job documents.
/// </summary>
public interface IJobStore
{
    /// <summary>
    /// Finds a Vesta job by its validated job ID.
    /// Returns null when the job does not exist.
    /// </summary>
    Task<StoredJob?> FindAsync(
        string jobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new Vesta job using the next available sequence
    /// for its Vulcan job ID, from t01 through t99.
    /// </summary>
    Task<StoredJob> CreateAsync(
        JobDocument job,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Replaces a job only when its current ETag matches expectedETag.
    /// Returns the updated job and its new ETag.
    /// </summary>
    Task<StoredJob> ReplaceAsync(
        JobDocument job,
        string expectedETag,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides write-once and read operations for files belonging to a Vesta job.
/// </summary>
public interface IJobFiles
{
    /// <summary>
    /// Writes a file only when it does not already exist.
    /// Returns false when the file already exists and must be kept unchanged.
    /// </summary>
    Task<bool> TryWriteNewAsync(
        string jobId,
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a file belonging to a Vesta job.
    /// Returns null when the file does not exist.
    /// </summary>
    Task<byte[]?> TryReadAsync(
        string jobId,
        string relativePath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides append-only audit logging for Vesta jobs.
/// </summary>
public interface IAuditTrail
{
    /// <summary>
    /// Appends one audit entry to the job's audit log.
    /// Existing audit entries must never be overwritten.
    /// </summary>
    Task AppendAsync(
        string jobId,
        AuditEntry entry,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Publishes messages requesting Vesta processing steps.
/// </summary>
public interface IStepSender
{
    /// <summary>
    /// Publishes a step message with its corresponding Service Bus application property.
    /// </summary>
    Task PublishAsync(
        StepMessage message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides read-only access to Vulcan job information and generated files.
/// </summary>
public interface IVulcanSource
{
    /// <summary>
    /// Reads the job document belonging to a Vulcan job.
    /// </summary>
    Task<byte[]?> ReadJobAsync(
        string vulcanJobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the package metadata belonging to a Vulcan job.
    /// </summary>
    Task<byte[]?> ReadPackageAsync(
        string vulcanJobId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a named ZIP file from Vulcan's output storage.
    /// </summary>
    Task<byte[]?> ReadZipAsync(
        string vulcanJobId,
        string zipPath,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides read-only access to Minerva test-case exports.
/// </summary>
public interface IMinervaSource
{
    /// <summary>
    /// Reads the test-case export for a Minerva run and package version.
    /// </summary>
    Task<byte[]?> ReadTestCasesAsync(
        string runId,
        int packageVersion,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Provides write-once storage for completed Vesta output ZIP files.
/// </summary>
public interface IOutputStore
{
    /// <summary>
    /// Writes the completed ZIP for a Vesta job.
    /// Existing output must never be overwritten.
    /// Returns the output ZIP path.
    /// </summary>
    Task<string> WriteNewZipAsync(
        string jobId,
        string fullName,
        ReadOnlyMemory<byte> zipContent,
        CancellationToken cancellationToken = default);
}
