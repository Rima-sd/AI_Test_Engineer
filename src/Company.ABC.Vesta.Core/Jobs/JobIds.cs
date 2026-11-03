
// Purpose: Validates Vulcan and Vesta job identifiers.
// Ensures IDs match the agreed patterns before they are used to build storage paths.

using System.Text.RegularExpressions;

namespace Company.ABC.Vesta.Core.Jobs;

/// <summary>
/// Provides validation and creation of Vesta and Vulcan job identifiers.
/// </summary>
public static partial class JobIds
{
    private const string VulcanPattern =
        @"^[a-z0-9]+(-[a-z0-9]+)*-code-[0-9]{8}-[0-9]{2}$";

    private const string VestaPattern =
        @"^[a-z0-9]+(-[a-z0-9]+)*-code-[0-9]{8}-[0-9]{2}-t[0-9]{2}$";

    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Checks whether the supplied Vulcan job ID matches the required pattern.
    /// </summary>
    public static bool IsValidVulcanJobId(string? jobId)
    {
        return !string.IsNullOrWhiteSpace(jobId)
            && Regex.IsMatch(jobId, VulcanPattern, RegexOptions.None, RegexTimeout);
    }

    /// <summary>
    /// Checks whether the supplied Vesta job ID matches the required pattern.
    /// </summary>
    public static bool IsValidVestaJobId(string? jobId)
    {
        return !string.IsNullOrWhiteSpace(jobId)
            && Regex.IsMatch(jobId, VestaPattern, RegexOptions.None, RegexTimeout);
    }

    /// <summary>
    /// Builds a Vesta job ID using a validated Vulcan job ID and sequence number.
    /// </summary>
    public static string CreateVestaJobId(string vulcanJobId, int sequence)
    {
        if (!IsValidVulcanJobId(vulcanJobId))
        {
            throw new ArgumentException(
                "The supplied Vulcan job ID is invalid.",
                nameof(vulcanJobId));
        }

        if (sequence is < 1 or > 99)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sequence),
                "The Vesta job sequence must be between 1 and 99.");
        }

        return $"{vulcanJobId}-t{sequence:D2}";
    }
}
