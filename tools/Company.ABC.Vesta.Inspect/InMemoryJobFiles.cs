// Purpose: In-memory IJobFiles implementation for Inspect test execution without storage accounts or Azurite.

using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Inspect;

internal sealed class InMemoryJobFiles : IJobFiles
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, byte[]> Files => _files;

    public Task<bool> TryWriteNewAsync(
        string jobId,
        string relativePath,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        string key = $"{jobId}/{relativePath}";
        if (_files.ContainsKey(key))
        {
            return Task.FromResult(false);
        }

        _files[key] = content.ToArray();
        return Task.FromResult(true);
    }

    public Task<byte[]?> TryReadAsync(
        string jobId,
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        string key = $"{jobId}/{relativePath}";
        _files.TryGetValue(key, out byte[]? bytes);
        return Task.FromResult(bytes);
    }
}