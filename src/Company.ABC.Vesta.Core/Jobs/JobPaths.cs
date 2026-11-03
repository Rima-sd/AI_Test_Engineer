
// Purpose: Centralises storage path construction for Vesta jobs and their related inputs and outputs.
// Validates job identifiers and relative file paths before constructing storage paths.

namespace Company.ABC.Vesta.Core.Jobs;

/// <summary>
/// Builds validated storage paths for Vesta jobs and related resources.
/// </summary>
public static class JobPaths
{
    /// <summary>
    /// Returns the path to the Vesta job document.
    /// </summary>
    public static string JobDocument(string jobId)
    {
        ValidateVestaJobId(jobId);
        return $"{jobId}/job.json";
    }

    /// <summary>
    /// Returns the path to the Vesta job audit log.
    /// </summary>
    public static string AuditLog(string jobId)
    {
        ValidateVestaJobId(jobId);
        return $"{jobId}/audit.log";
    }

    /// <summary>
    /// Returns a validated file path inside a Vesta job.
    /// </summary>
    public static string JobFile(string jobId, string relativePath)
    {
        ValidateVestaJobId(jobId);
        ValidateRelativePath(relativePath);

        if (relativePath.Equals("job.json", StringComparison.OrdinalIgnoreCase)
            || relativePath.Equals("audit.log", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "The job document and audit log cannot be written as ordinary job files.",
                nameof(relativePath));
        }

        return $"{jobId}/{relativePath}";
    }

    /// <summary>
    /// Returns the path to the Vulcan job document.
    /// </summary>
    public static string VulcanJobDocument(string vulcanJobId)
    {
        ValidateVulcanJobId(vulcanJobId);
        return $"{vulcanJobId}/job.json";
    }

    /// <summary>
    /// Returns the path to a package in a Vulcan job.
    /// </summary>
    public static string VulcanPackage(string vulcanJobId, string packageName)
    {
        ValidateVulcanJobId(vulcanJobId);
        ValidateRelativePath(packageName);

        return $"{vulcanJobId}/{packageName}";
    }

    /// <summary>
    /// Returns the path to Minerva test cases for a mapping.
    /// </summary>
    public static string MinervaTestCases(string mappingId)
    {
        if (string.IsNullOrWhiteSpace(mappingId)
            || mappingId.Contains('/')
            || mappingId.Contains('\\')
            || mappingId is "." or "..")
        {
            throw new ArgumentException(
                "The mapping ID is invalid.",
                nameof(mappingId));
        }

        return $"{mappingId}/test-cases.json";
    }

    /// <summary>
    /// Returns the path for a Vesta output ZIP.
    /// </summary>
    public static string OutputZip(string jobId, int round)
    {
        ValidateVestaJobId(jobId);

        if (round is < 0 or > 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(round),
                "The processing round must be between 0 and 3.");
        }

        return $"{jobId}/output/round-{round}.zip";
    }

    private static void ValidateVestaJobId(string jobId)
    {
        if (!JobIds.IsValidVestaJobId(jobId))
        {
            throw new ArgumentException(
                "The Vesta job ID is invalid.",
                nameof(jobId));
        }
    }

    private static void ValidateVulcanJobId(string vulcanJobId)
    {
        if (!JobIds.IsValidVulcanJobId(vulcanJobId))
        {
            throw new ArgumentException(
                "The Vulcan job ID is invalid.",
                nameof(vulcanJobId));
        }
    }

    private static void ValidateRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Contains('\\')
            || relativePath.StartsWith('/')
            || relativePath.EndsWith('/'))
        {
            throw new ArgumentException(
                "The relative path is invalid.",
                nameof(relativePath));
        }

        string[] parts = relativePath.Split('/');

        if (parts.Any(part => string.IsNullOrWhiteSpace(part)
            || part is "." or ".."))
        {
            throw new ArgumentException(
                "The relative path contains an invalid path segment.",
                nameof(relativePath));
        }
    }
}
