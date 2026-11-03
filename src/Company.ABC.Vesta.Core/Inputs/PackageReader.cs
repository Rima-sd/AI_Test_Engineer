// Purpose: Reads and validates Vulcan's three-file package and its mapping contract.

using System.Text.Json;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record PackageFile(
    string FileName,
    string ContentType,
    string Content,
    string Remarks);

public sealed record MappingRow(
    string RowId,
    string ScopeId,
    string TargetPath,
    string RuleType);

public sealed record ParsedMapping(
    string ContractVersion,
    string MappingId,
    JsonElement Scopes,
    IReadOnlyList<MappingRow> Rows,
    JsonElement RawMapping);

public sealed record PackageReadResult(
    IReadOnlyList<PackageFile> Files,
    PackageFile? SourceFile,
    PackageFile? TargetFile,
    PackageFile? MappingFile,
    ParsedMapping? Mapping,
    string? ErrorCode,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => ErrorCode is null;
}

public static class PackageReader
{
    private const string ExpectedContractVersion = "0.1";

    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64
    };

    public static PackageReadResult Read(ReadOnlyMemory<byte>? content)
    {
        if (content is null || content.Value.IsEmpty)
        {
            return InvalidPackage("Package content is missing.");
        }

        try
        {
            using var document = JsonDocument.Parse(content.Value, DocumentOptions);
            return ReadPackageDocument(document.RootElement);
        }
        catch (JsonException exception)
        {
            return InvalidPackage($"Package JSON is invalid: {exception.Message}");
        }
    }

    private static PackageReadResult ReadPackageDocument(JsonElement root)
    {
        var problems = new List<string>();

        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(root, "Files", out var filesElement) ||
            filesElement.ValueKind != JsonValueKind.Array)
        {
            return InvalidPackage("Package must contain a Files array.");
        }

        var files = ReadFiles(filesElement, problems);
        CheckFileCount(files, problems);

        var sourceFiles = FindByRemarks(files, "source");
        var targetFiles = FindByRemarks(files, "target");
        var mappingFiles = FindByRemarks(files, "mapping");

        CheckUniqueEntry(sourceFiles, "source", problems);
        CheckUniqueEntry(targetFiles, "target", problems);
        CheckUniqueEntry(mappingFiles, "mapping", problems);

        var sourceFile = sourceFiles.Count == 1 ? sourceFiles[0] : null;
        var targetFile = targetFiles.Count == 1 ? targetFiles[0] : null;
        var mappingFile = mappingFiles.Count == 1 ? mappingFiles[0] : null;

        var mapping = mappingFile is null
            ? null
            : ReadMapping(mappingFile.Content, problems);

        return CreateResult(
            files,
            sourceFile,
            targetFile,
            mappingFile,
            mapping,
            problems);
    }

    private static List<PackageFile> ReadFiles(
        JsonElement filesElement,
        List<string> problems)
    {
        var files = new List<PackageFile>();

        foreach (var element in filesElement.EnumerateArray())
        {
            if (TryReadFile(element, out var file))
            {
                files.Add(file);
            }
            else
            {
                problems.Add(
                    "Each Files entry must contain string FileName, ContentType, Content and Remarks.");
            }
        }

        return files;
    }

    private static bool TryReadFile(
        JsonElement element,
        out PackageFile file)
    {
        file = null!;

        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetString(element, "FileName", out var fileName) ||
            !TryGetString(element, "ContentType", out var contentType) ||
            !TryGetString(element, "Content", out var content) ||
            !TryGetString(element, "Remarks", out var remarks))
        {
            return false;
        }

        file = new PackageFile(fileName, contentType, content, remarks);
        return true;
    }

    private static void CheckFileCount(
        List<PackageFile> files,
        List<string> problems)
    {
        if (files.Count != 3)
        {
            problems.Add(
                $"Package must contain exactly three valid Files entries; found {files.Count}.");
        }
    }

    private static List<PackageFile> FindByRemarks(
        List<PackageFile> files,
        string keyword)
    {
        return files
            .Where(file => file.Remarks.Contains(
                keyword,
                StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    private static void CheckUniqueEntry(
        List<PackageFile> matches,
        string keyword,
        List<string> problems)
    {
        if (matches.Count != 1)
        {
            problems.Add(
                $"Package must contain exactly one entry whose Remarks contains '{keyword}'; found {matches.Count}.");
        }
    }

    private static ParsedMapping? ReadMapping(
        string mappingContent,
        List<string> problems)
    {
        try
        {
            using var document = JsonDocument.Parse(
                mappingContent,
                DocumentOptions);

            return ParseMapping(document.RootElement, problems);
        }
        catch (JsonException exception)
        {
            problems.Add($"Mapping content is invalid JSON: {exception.Message}");
            return null;
        }
    }

    private static ParsedMapping? ParseMapping(
        JsonElement root,
        List<string> problems)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            problems.Add("Mapping content must be a JSON object.");
            return null;
        }

        var contractVersion = ReadRequiredString(
            root, "contractVersion", problems);

        var mappingId = ReadRequiredString(
            root, "mappingId", problems);

        var scopes = ReadRequiredArray(
            root, "scopes", problems);

        var rowsElement = ReadRequiredArray(
            root, "rows", problems);

        var rows = rowsElement.ValueKind == JsonValueKind.Array
            ? ReadRows(rowsElement, problems)
            : new List<MappingRow>();

        if (contractVersion is not null &&
            !string.Equals(
                contractVersion,
                ExpectedContractVersion,
                StringComparison.Ordinal))
        {
            problems.Add(
                $"Mapping contractVersion must be '{ExpectedContractVersion}'.");
        }

        if (rows.Count == 0)
        {
            problems.Add("Mapping must contain at least one valid row.");
        }

        if (contractVersion is null ||
            mappingId is null ||
            scopes.ValueKind != JsonValueKind.Array ||
            rowsElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return new ParsedMapping(
            contractVersion,
            mappingId,
            scopes.Clone(),
            rows,
            root.Clone());
    }

    private static List<MappingRow> ReadRows(
        JsonElement rowsElement,
        List<string> problems)
    {
        var rows = new List<MappingRow>();

        foreach (var rowElement in rowsElement.EnumerateArray())
        {
            if (TryReadRow(rowElement, out var row))
            {
                rows.Add(row);
            }
            else
            {
                problems.Add(
                    "Each mapping row must contain rowId, scopeId, target.path and rule.type.");
            }
        }

        return rows;
    }

    private static bool TryReadRow(
        JsonElement element,
        out MappingRow row)
    {
        row = null!;

        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetString(element, "rowId", out var rowId) ||
            !TryGetString(element, "scopeId", out var scopeId) ||
            !TryGetProperty(element, "target", out var target) ||
            !TryGetString(target, "path", out var targetPath) ||
            !TryGetProperty(element, "rule", out var rule) ||
            !TryGetString(rule, "type", out var ruleType))
        {
            return false;
        }

        row = new MappingRow(rowId, scopeId, targetPath, ruleType);
        return true;
    }

    private static string? ReadRequiredString(
        JsonElement element,
        string propertyName,
        List<string> problems)
    {
        if (TryGetString(element, propertyName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        problems.Add($"Mapping must contain a non-empty '{propertyName}' string.");
        return null;
    }

    private static JsonElement ReadRequiredArray(
        JsonElement element,
        string propertyName,
        List<string> problems)
    {
        if (TryGetProperty(element, propertyName, out var value) &&
            value.ValueKind == JsonValueKind.Array)
        {
            return value;
        }

        problems.Add($"Mapping must contain a '{propertyName}' array.");
        return default;
    }

    private static bool TryGetString(
     JsonElement element,
     string propertyName,
     out string value)
    {
        value = string.Empty;

        if (!TryGetProperty(element, propertyName, out var property) ||
            property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var result = property.GetString();

        if (result is null)
        {
            return false;
        }

        value = result;
        return true;
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        value = default;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var match = element.EnumerateObject()
            .Where(property => string.Equals(
                property.Name,
                propertyName,
                StringComparison.OrdinalIgnoreCase))
            .Select(property => property.Value)
            .FirstOrDefault();

        if (match.ValueKind == JsonValueKind.Undefined)
        {
            return false;
        }

        value = match;
        return true;
    }
    private static PackageReadResult InvalidPackage(string problem)
    {
        return new PackageReadResult(
            Array.Empty<PackageFile>(),
            null,
            null,
            null,
            null,
            JobErrorCodes.PackageInvalid,
            new List<string> { problem });
    }

    private static PackageReadResult CreateResult(
        List<PackageFile> files,
        PackageFile? sourceFile,
        PackageFile? targetFile,
        PackageFile? mappingFile,
        ParsedMapping? mapping,
        List<string> problems)
    {
        return new PackageReadResult(
            files,
            sourceFile,
            targetFile,
            mappingFile,
            mapping,
            problems.Count == 0 ? null : JobErrorCodes.PackageInvalid,
            problems);
    }
}