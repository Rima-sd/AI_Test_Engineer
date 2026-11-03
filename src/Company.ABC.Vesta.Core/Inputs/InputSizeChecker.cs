// Purpose: Enforces the configured maximum size for agent input content.

using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record InputSizeCheckResult(
    long TotalBytes,
    long MaximumBytes,
    string? ErrorCode,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => ErrorCode is null;
}

public static class InputSizeChecker
{
    private static readonly string[] GeneratedFilePaths =
    [
        "Models/InputDTO.cs",
        "Models/OutputDTO.cs",
        "Transformation/Mapping.cs",
        "Transformation/MappingLogic.md"
    ];

    public static InputSizeCheckResult Check(
        ReadOnlyMemory<byte> packageContent,
        ProjectZip projectZip,
        InputLimits limits)
    {
        ArgumentNullException.ThrowIfNull(projectZip);
        ArgumentNullException.ThrowIfNull(limits);

        long totalBytes = packageContent.Length;

        foreach (var path in GeneratedFilePaths)
        {
            if (projectZip.Files.TryGetValue(path, out var content))
            {
                totalBytes += content.Length;
            }
        }

        return CreateResult(totalBytes, limits.MaxAgentInputBytes);
    }

    private static InputSizeCheckResult CreateResult(
        long totalBytes,
        long maximumBytes)
    {
        if (totalBytes <= maximumBytes)
        {
            return new InputSizeCheckResult(
                totalBytes,
                maximumBytes,
                null,
                Array.Empty<string>());
        }

        var problem =
            $"Agent input is {totalBytes} bytes; the configured maximum is {maximumBytes} bytes.";

        return new InputSizeCheckResult(
            totalBytes,
            maximumBytes,
            JobErrorCodes.InputTooLarge,
            new List<string> { problem });
    }
}