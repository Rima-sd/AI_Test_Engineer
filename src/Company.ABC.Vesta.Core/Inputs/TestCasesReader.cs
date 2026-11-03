// Purpose: Reads Minerva test cases and validates their mapping row references.

using System.Text.Json;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record MappingTestCase(
    string RowId,
    string TargetPath,
    JsonElement Input,
    JsonElement Expected,
    bool Synthetic);

public sealed record TestCasesReadResult(
    bool Found,
    IReadOnlyList<MappingTestCase> Cases,
    string? ErrorCode,
    IReadOnlyList<string> Problems)
{
    public bool IsValid => ErrorCode is null;
}

public static class TestCasesReader
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        MaxDepth = 64
    };

    public static TestCasesReadResult Read(
        ReadOnlyMemory<byte>? content,
        ParsedMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        if (content is null)
        {
            return new TestCasesReadResult(
                false,
                Array.Empty<MappingTestCase>(),
                null,
                Array.Empty<string>());
        }

        try
        {
            using var document = JsonDocument.Parse(
                content.Value,
                DocumentOptions);

            return ParseCases(document.RootElement, mapping);
        }
        catch (JsonException exception)
        {
            return Invalid($"Test cases JSON is invalid: {exception.Message}");
        }
    }

    private static TestCasesReadResult ParseCases(
        JsonElement root,
        ParsedMapping mapping)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(root, "cases", out var casesElement) ||
            casesElement.ValueKind != JsonValueKind.Array)
        {
            return Invalid("Test cases must contain a cases array.");
        }

        var knownRowIds = mapping.Rows
            .Select(row => row.RowId)
            .ToHashSet(StringComparer.Ordinal);

        var cases = new List<MappingTestCase>();
        var problems = new List<string>();

        foreach (var element in casesElement.EnumerateArray())
        {
            ReadCase(element, knownRowIds, cases, problems);
        }

        return CreateResult(cases, problems);
    }

    private static void ReadCase(
        JsonElement element,
        HashSet<string> knownRowIds,
        List<MappingTestCase> cases,
        List<string> problems)
    {
        if (!TryReadCase(element, out var testCase))
        {
            problems.Add(
                "Each test case must contain rowId, targetPath, input and expected.");
            return;
        }

        if (!knownRowIds.Contains(testCase.RowId))
        {
            problems.Add(
                $"Test case rowId '{testCase.RowId}' does not exist in the mapping.");
            return;
        }

        cases.Add(testCase);
    }

    private static bool TryReadCase(
        JsonElement element,
        out MappingTestCase testCase)
    {
        testCase = null!;

        if (element.ValueKind != JsonValueKind.Object ||
            !TryGetString(element, "rowId", out var rowId) ||
            !TryGetString(element, "targetPath", out var targetPath) ||
            !TryGetProperty(element, "input", out var input) ||
            !TryGetProperty(element, "expected", out var expected))
        {
            return false;
        }

        var synthetic = false;

        if (TryGetProperty(element, "synthetic", out var syntheticElement))
        {
            if (syntheticElement.ValueKind is not (
                JsonValueKind.True or JsonValueKind.False))
            {
                return false;
            }

            synthetic = syntheticElement.GetBoolean();
        }

        testCase = new MappingTestCase(
            rowId,
            targetPath,
            input.Clone(),
            expected.Clone(),
            synthetic);

        return true;
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

        if (string.IsNullOrWhiteSpace(result))
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

    private static TestCasesReadResult CreateResult(
        List<MappingTestCase> cases,
        List<string> problems)
    {
        return new TestCasesReadResult(
            true,
            cases,
            problems.Count == 0 ? null : JobErrorCodes.TestCasesInvalid,
            problems);
    }

    private static TestCasesReadResult Invalid(string problem)
    {
        return new TestCasesReadResult(
            true,
            Array.Empty<MappingTestCase>(),
            JobErrorCodes.TestCasesInvalid,
            new List<string> { problem });
    }
}