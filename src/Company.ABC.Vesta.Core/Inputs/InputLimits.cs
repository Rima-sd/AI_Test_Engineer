
// Purpose: Defines immutable input-processing limits supplied by application configuration.

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record InputLimits
{
    public InputLimits(
        long maxZipBytes,
        int maxZipEntries,
        long maxZipUnpackedBytes,
        int maxAgentInputBytes,
        int maxTestFileBytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxZipBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxZipEntries);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxZipUnpackedBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAgentInputBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTestFileBytes);

        MaxZipBytes = maxZipBytes;
        MaxZipEntries = maxZipEntries;
        MaxZipUnpackedBytes = maxZipUnpackedBytes;
        MaxAgentInputBytes = maxAgentInputBytes;
        MaxTestFileBytes = maxTestFileBytes;
    }

    public long MaxZipBytes { get; }
    public int MaxZipEntries { get; }
    public long MaxZipUnpackedBytes { get; }
    public int MaxAgentInputBytes { get; }
    public int MaxTestFileBytes { get; }
}
