// Purpose: Reads ZIP archives safely in memory and validates their entries and size limits.

using System.IO.Compression;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record ProjectZipReadResult(
    ProjectZip? Zip,
    string? ErrorCode,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Zip is not null && ErrorCode is null;

    public static ProjectZipReadResult Success(ProjectZip zip) =>
        new(zip, null, Array.Empty<string>());

    public static ProjectZipReadResult Failure(
        string errorCode,
        IReadOnlyList<string> problems) =>
        new(null, errorCode, problems);
}

public static class ProjectZipReader
{
    private const int BufferSize = 81920;
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixSymbolicLink = 0xA000;
    private const string CompileErrorsFile = "COMPILE-ERRORS.md";

    public static ProjectZipReadResult Read(
        ReadOnlyMemory<byte>? content,
        InputLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);

        if (content is null || content.Value.IsEmpty)
        {
            return Failure(
                JobErrorCodes.ZipNotFound,
                "The project ZIP was not found or is empty.");
        }

        if (content.Value.Length > limits.MaxZipBytes)
        {
            return Failure(
                JobErrorCodes.ZipUnsafe,
                $"ZIP size exceeds the configured limit of {limits.MaxZipBytes} bytes.");
        }

        try
        {
            using MemoryStream stream = new(
                content.Value.ToArray(),
                writable: false);

            using ZipArchive archive = new(
                stream,
                ZipArchiveMode.Read,
                leaveOpen: false);

            return ReadEntries(archive, limits);
        }
        catch (InvalidDataException)
        {
            return Failure(
                JobErrorCodes.ZipUnsafe,
                "The ZIP archive is invalid or corrupted.");
        }
        catch (IOException)
        {
            return Failure(
                JobErrorCodes.ZipUnsafe,
                "The ZIP archive could not be read.");
        }
    }

    private static ProjectZipReadResult ReadEntries(
        ZipArchive archive,
        InputLimits limits)
    {
        if (archive.Entries.Count > limits.MaxZipEntries)
        {
            return Failure(
                JobErrorCodes.ZipUnsafe,
                $"ZIP contains more than {limits.MaxZipEntries} entries.");
        }

        List<ZipEntryInfo> entries = [];
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        List<string> problems = [];

        foreach (ZipArchiveEntry entry in archive.Entries)
        {
            if (!TryValidateEntry(entry, names, out string? problem))
            {
                problems.Add(problem!);
                continue;
            }

            entries.Add(new ZipEntryInfo(
                entry,
                entry.FullName,
                IsDirectory(entry)));
        }

        if (problems.Count > 0)
        {
            return ProjectZipReadResult.Failure(
                JobErrorCodes.ZipUnsafe,
                problems);
        }

        string? topFolderName = FindTopFolder(entries);

        Dictionary<string, ReadOnlyMemory<byte>> files =
            new(StringComparer.OrdinalIgnoreCase);

        long totalUnpackedBytes = 0;

        foreach (ZipEntryInfo entry in entries)
        {
            if (entry.IsDirectoryEntry)
            {
                continue;
            }

            string path = RemoveTopFolder(
                entry.Path,
                topFolderName);

            if (string.IsNullOrWhiteSpace(path))
            {
                return Failure(
                    JobErrorCodes.ZipUnsafe,
                    $"ZIP entry '{entry.Path}' does not have a valid file path.");
            }

            if (string.Equals(
                path,
                CompileErrorsFile,
                StringComparison.OrdinalIgnoreCase))
            {
                return Failure(
                    JobErrorCodes.CodeDoesNotCompile,
                    "The ZIP contains COMPILE-ERRORS.md at its root.");
            }

            byte[] bytes = ReadEntry(
                entry.ZipEntry,
                limits.MaxZipUnpackedBytes,
                ref totalUnpackedBytes);

            if (!files.TryAdd(path, bytes))
            {
                return Failure(
                    JobErrorCodes.ZipUnsafe,
                    $"Duplicate ZIP file path '{path}'.");
            }
        }

        ProjectZip zip = new(
            files,
            totalUnpackedBytes,
            topFolderName);

        return ProjectZipReadResult.Success(zip);
    }

    private static bool TryValidateEntry(
        ZipArchiveEntry entry,
        HashSet<string> names,
        out string? problem)
    {
        problem = null;
        string path = entry.FullName;

        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\') ||
            path.Contains(':'))
        {
            problem = $"ZIP entry '{path}' contains an unsafe path.";
            return false;
        }

        bool isDirectory = IsDirectory(entry);

        string checkedPath = isDirectory
            ? path[..^1]
            : path;

        string[] parts = checkedPath.Split('/');

        if (parts.Length == 0 ||
            parts.Any(part =>
                string.IsNullOrEmpty(part) ||
                part is "." or ".."))
        {
            problem = $"ZIP entry '{path}' contains an unsafe path segment.";
            return false;
        }

        if (IsSymbolicLink(entry))
        {
            problem = $"ZIP entry '{path}' is a symbolic link.";
            return false;
        }

        if (!names.Add(path))
        {
            problem = $"ZIP contains duplicate entry name '{path}'.";
            return false;
        }

        return true;
    }

    private static byte[] ReadEntry(
        ZipArchiveEntry entry,
        long maximumUnpackedBytes,
        ref long totalUnpackedBytes)
    {
        using Stream input = entry.Open();
        using MemoryStream output = new();

        byte[] buffer = new byte[BufferSize];

        while (true)
        {
            int bytesRead = input.Read(
                buffer,
                0,
                buffer.Length);

            if (bytesRead == 0)
            {
                break;
            }

            totalUnpackedBytes += bytesRead;

            if (totalUnpackedBytes > maximumUnpackedBytes)
            {
                throw new InvalidDataException(
                    $"Unpacked ZIP size exceeds the configured limit of {maximumUnpackedBytes} bytes.");
            }

            output.Write(buffer, 0, bytesRead);
        }

        return output.ToArray();
    }

    private static string? FindTopFolder(
        IReadOnlyList<ZipEntryInfo> entries)
    {
        if (entries.Count == 0)
        {
            return null;
        }

        string? candidate = null;

        foreach (ZipEntryInfo entry in entries)
        {
            string path = entry.IsDirectoryEntry
                ? entry.Path[..^1]
                : entry.Path;

            int separatorIndex = path.IndexOf('/');

            if (separatorIndex < 0)
            {
                return null;
            }

            string firstPart = path[..separatorIndex];

            if (candidate is null)
            {
                candidate = firstPart;
            }
            else if (!string.Equals(
                candidate,
                firstPart,
                StringComparison.Ordinal))
            {
                return null;
            }
        }

        return candidate;
    }

    private static string RemoveTopFolder(
        string path,
        string? topFolderName)
    {
        if (topFolderName is null)
        {
            return path;
        }

        string prefix = $"{topFolderName}/";

        return path.StartsWith(
            prefix,
            StringComparison.Ordinal)
            ? path[prefix.Length..]
            : path;
    }

    private static bool IsDirectory(ZipArchiveEntry entry) =>
        entry.FullName.EndsWith('/');

    private static bool IsSymbolicLink(ZipArchiveEntry entry)
    {
        int unixFileType =
            (entry.ExternalAttributes >> 16) & UnixFileTypeMask;

        return unixFileType == UnixSymbolicLink;
    }

    private static ProjectZipReadResult Failure(
        string errorCode,
        string problem) =>
        ProjectZipReadResult.Failure(
            errorCode,
            [problem]);

    private sealed record ZipEntryInfo(
        ZipArchiveEntry ZipEntry,
        string Path,
       bool IsDirectoryEntry);
}