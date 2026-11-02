
/*
 * Purpose:
 * Parses the test code returned by the Azure AI Foundry agent,
 * validates the generated file path, and saves the new test file
 * without overwriting existing files or allowing path traversal.
 */

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TestEngineerPOC.Services;

public sealed class GeneratedTestFileService
{
    // Extracts the requested relative path and C# test code from the agent response.
    public (string RelativePath, string Code) ParseResponse(string response)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(response);

        var pathMatch = Regex.Match(
            response,
            @"TEST_FILE_PATH:\s*(.+)",
            RegexOptions.IgnoreCase);

        if (!pathMatch.Success)
            throw new InvalidDataException(
                "The agent response does not contain TEST_FILE_PATH.");

        var relativePath = pathMatch.Groups[1].Value
            .Trim()
            .Replace('\\', '/');

        ValidateRelativePath(relativePath);

        var codeMatch = Regex.Match(
            response,
            @"```csharp\s*(.*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        if (!codeMatch.Success)
            throw new InvalidDataException(
                "The agent response does not contain a C# code block.");

        var code = codeMatch.Groups[1].Value.Trim();

        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidDataException(
                "The generated C# test code is empty.");

        return (relativePath, code);
    }

    // Saves the generated file inside the test project without replacing an existing file.
    public string SaveNewTestFile(
        string testProjectRoot,
        string relativePath,
        string code)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testProjectRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        ArgumentNullException.ThrowIfNull(code);

        var root = Path.GetFullPath(testProjectRoot);

        ValidateRelativePath(relativePath);

        // Normalize the path returned by Foundry against the actual test project.
        var normalizedRelativePath = NormalizeRelativePath(
            relativePath,
            Path.GetFileName(root));

        var destination = Path.GetFullPath(
            Path.Combine(root, normalizedRelativePath));

        var rootPrefix = root.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;

        if (!destination.StartsWith(
                rootPrefix,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                "The generated test file escapes the test project.");
        }

        // CreateNew fails if the destination file already exists.
        Directory.CreateDirectory(
            Path.GetDirectoryName(destination)!);

        using var stream = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None);

        using var writer = new StreamWriter(stream);
        writer.Write(code);

        return destination;
    }

    // Removes an unnecessary project-folder prefix from the agent-generated path.
    private static string NormalizeRelativePath(
        string relativePath,
        string testProjectDirectoryName)
    {
        var segments = relativePath.Split('/');

        if (segments.Length > 1)
        {
            var firstSegment = segments[0];

            // Examples:
            // Tests/Transformation/MappingTests.cs
            // adapter.Tests/Transformation/MappingTests.cs
            //
            // Both should become:
            // Transformation/MappingTests.cs
            if (firstSegment.Equals(
                    testProjectDirectoryName,
                    StringComparison.OrdinalIgnoreCase) ||
                firstSegment.EndsWith(
                    ".Tests",
                    StringComparison.OrdinalIgnoreCase))
            {
                segments = segments.Skip(1).ToArray();
            }
        }

        if (segments.Length == 0)
            throw new InvalidDataException(
                "The generated test file path is invalid.");

        var normalizedPath = Path.Combine(segments);

        ValidateRelativePath(normalizedPath.Replace('\\', '/'));

        return normalizedPath;
    }

    // Rejects rooted paths and traversal segments such as ../.
    private static void ValidateRelativePath(string relativePath)
    {
        var normalizedPath = relativePath.Replace('\\', '/');

        if (Path.IsPathRooted(relativePath) ||
            !normalizedPath.EndsWith(
                ".cs",
                StringComparison.OrdinalIgnoreCase) ||
            normalizedPath.Contains(':') ||
            normalizedPath.Split('/').Any(
                segment => segment is "." or ".." or ""))
        {
            throw new InvalidDataException(
                "The generated test file path is invalid.");
        }
    }
}
