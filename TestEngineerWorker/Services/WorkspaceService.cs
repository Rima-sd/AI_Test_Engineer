
/*
 * Purpose:
 * Extracts project ZIP files into a dedicated validation workspace.
 *
 * Security:
 * - Rejects ZIP path traversal attempts.
 * - Ensures extraction stays inside the workspace.
 * - Requires an empty workspace.
 * - Prevents overwriting existing files.
 */

using System.IO.Compression;

namespace TestEngineerWorker.Services;

public sealed class WorkspaceService : IWorkspaceService
{
    public void ExtractZipSafely(
        string zipFilePath,
        string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipFilePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);

        if (!File.Exists(zipFilePath))
        {
            throw new FileNotFoundException(
                "The input project ZIP was not found.",
                zipFilePath);
        }

        var root = Path.GetFullPath(workspaceRoot);
        Directory.CreateDirectory(root);

        // Each attempt must use a fresh, empty workspace.
        if (Directory.EnumerateFileSystemEntries(root).Any())
        {
            throw new InvalidOperationException(
                "The validation workspace must be empty.");
        }

        var rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        using var archive = ZipFile.OpenRead(zipFilePath);

        foreach (var entry in archive.Entries)
        {
            var entryName = entry.FullName.Replace('\\', '/');

            // Reject unsafe ZIP entry paths.
            if (string.IsNullOrWhiteSpace(entryName) ||
                entryName.StartsWith('/') ||
                entryName.Contains(':') ||
                entryName.Split('/').Any(
                    segment => segment is "." or ".."))
            {
                throw new InvalidDataException(
                    $"Unsafe ZIP entry detected: {entry.FullName}");
            }

            var destination = Path.GetFullPath(
                Path.Combine(
                    root,
                    entryName.Replace(
                        '/',
                        Path.DirectorySeparatorChar)));

            // Ensure the destination remains inside the workspace.
            if (!destination.StartsWith(
                    rootPrefix,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"ZIP entry escapes the workspace: {entry.FullName}");
            }

            // Create directory entries.
            if (entry.FullName.EndsWith('/'))
            {
                Directory.CreateDirectory(destination);
                continue;
            }

            var destinationDirectory =
                Path.GetDirectoryName(destination)!;

            Directory.CreateDirectory(destinationDirectory);

            // CreateNew prevents overwriting an existing file.
            using var source = entry.Open();

            using var target = new FileStream(
                destination,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None);

            source.CopyTo(target);
        }
    }
}
