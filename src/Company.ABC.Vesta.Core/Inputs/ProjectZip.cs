// Purpose: Represents a validated project ZIP archive held in memory.

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record ProjectZip(
    IReadOnlyDictionary<string, ReadOnlyMemory<byte>> Files,
    long TotalUnpackedBytes,
    string? TopFolderName);