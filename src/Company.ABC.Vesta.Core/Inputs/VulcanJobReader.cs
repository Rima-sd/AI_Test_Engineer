// Purpose: Reads and validates the fields Vesta needs from a Vulcan job.json document.

using System.Text.Json;
using Company.ABC.Vesta.Core.Jobs;

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record VulcanJobReadResult(
    VulcanJob? Job,
    string? ErrorCode,
    IReadOnlyList<string> Problems)
{
    public bool Succeeded => Job is not null && ErrorCode is null;

    public static VulcanJobReadResult Success(VulcanJob job) =>
        new(job, null, Array.Empty<string>());

    public static VulcanJobReadResult Failure(
        string errorCode,
        IReadOnlyList<string> problems) =>
        new(null, errorCode, problems);
}

public static class VulcanJobReader
{
    private const string NotFoundCode = "VulcanJobNotFound";
    private const string NotDoneCode = "VulcanJobNotDone";
    private const string CompileErrorCode = "CodeDoesNotCompile";
    private const string InvalidCode = "ContractViolation";
    private static bool IsValidJobId(string value)
    {
        return JobIds.IsValidVulcanJobId(value);
    }

    public static VulcanJobReadResult Read(ReadOnlyMemory<byte>? content)
    {
        if (content is null || content.Value.IsEmpty)
        {
            return VulcanJobReadResult.Failure(
                NotFoundCode,
                ["Vulcan job.json was not found or was empty."]);
        }

        try
        {
            using JsonDocument document = JsonDocument.Parse(content.Value);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Invalid("The job document must be a JSON object.");
            }

            return ReadJob(document.RootElement);
        }
        catch (JsonException)
        {
            return Invalid("Vulcan job.json contains invalid JSON.");
        }
    }

    private static VulcanJobReadResult ReadJob(JsonElement root)
    {
        List<string> problems = [];

        string jobId = ReadString(root, "jobId", problems);
        string mappingId = ReadString(root, "mappingId", problems);
        string state = ReadString(root, "state", problems);

        JsonElement origin = ReadObject(root, "origin", problems);
        string originKind = ReadString(origin, "kind", problems);
        string? runId = ReadNullableString(origin, "runId", problems);
        int? packageVersion = ReadNullableInt(origin, "packageVersion", problems);

        JsonElement project = ReadObject(root, "project", problems);
        string projectFullName = ReadString(project, "fullName", problems);

        int generation = ReadInt(root, "generation", problems);

        JsonElement output = ReadObject(root, "output", problems);
        string? zipPath = ReadNullableString(output, "zipPath", problems);
        bool compiled = ReadBool(output, "compiled", problems);
        int outputGeneration = ReadInt(output, "generation", problems);

        ValidateFields(
            jobId,
            mappingId,
            state,
            originKind,
            runId,
            packageVersion,
            projectFullName,
            generation,
            zipPath,
            outputGeneration,
            problems);

        if (problems.Count > 0)
        {
            return VulcanJobReadResult.Failure(InvalidCode, problems);
        }

        if (!string.Equals(state, "Done", StringComparison.Ordinal))
        {
            return VulcanJobReadResult.Failure(
                NotDoneCode,
                [$"Vulcan job state is '{state}', not 'Done'."]);
        }

        if (!compiled)
        {
            return VulcanJobReadResult.Failure(
                CompileErrorCode,
                ["Vulcan reported output.compiled as false."]);
        }

        VulcanJob job = new(
            jobId,
            mappingId,
            originKind,
            runId,
            packageVersion,
            projectFullName,
            state,
            generation,
            zipPath,
            compiled,
            outputGeneration);

        return VulcanJobReadResult.Success(job);
    }

    private static void ValidateFields(
        string jobId,
        string mappingId,
        string state,
        string originKind,
        string? runId,
        int? packageVersion,
        string projectFullName,
        int generation,
        string? zipPath,
        int outputGeneration,
        List<string> problems)
    {
        if (!IsValidJobId(jobId))
        {
            problems.Add("jobId does not match the Vulcan job ID format.");
        }

        if (string.IsNullOrWhiteSpace(mappingId))
        {
            problems.Add("mappingId must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(projectFullName))
        {
            problems.Add("project.fullName must not be empty.");
        }

        if (generation < 1)
        {
            problems.Add("generation must be greater than zero.");
        }

        if (outputGeneration < 1)
        {
            problems.Add("output.generation must be greater than zero.");
        }

        if (string.Equals(state, "Done", StringComparison.Ordinal) &&
            string.IsNullOrWhiteSpace(zipPath))
        {
            problems.Add("output.zipPath must not be empty when the job is Done.");
        }

        if (string.Equals(originKind, "Minerva", StringComparison.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(runId))
            {
                problems.Add("origin.runId is required for Minerva origin.");
            }

            if (packageVersion is null or < 1)
            {
                problems.Add("origin.packageVersion must be greater than zero for Minerva origin.");
            }
        }
        else if (string.Equals(originKind, "Upload", StringComparison.Ordinal))
        {
            if (runId is not null || packageVersion is not null)
            {
                problems.Add("Upload origin requires null runId and packageVersion.");
            }
        }
        else
        {
            problems.Add("origin.kind must be Minerva or Upload.");
        }
    }

    private static string ReadString(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{propertyName} is required and must be a string.");
            return string.Empty;
        }

        return value.GetString() ?? string.Empty;
    }

    private static string? ReadNullableString(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            problems.Add($"{propertyName} is required.");
            return null;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.String)
        {
            problems.Add($"{propertyName} must be a string or null.");
            return null;
        }

        return value.GetString();
    }

    private static int ReadInt(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            !value.TryGetInt32(out int result))
        {
            problems.Add($"{propertyName} is required and must be an integer.");
            return 0;
        }

        return result;
    }

    private static int? ReadNullableInt(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value))
        {
            problems.Add($"{propertyName} is required.");
            return null;
        }

        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (!value.TryGetInt32(out int result))
        {
            problems.Add($"{propertyName} must be an integer or null.");
            return null;
        }

        return result;
    }

    private static bool ReadBool(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            problems.Add($"{propertyName} is required and must be a boolean.");
            return false;
        }

        return value.GetBoolean();
    }

    private static JsonElement ReadObject(
        JsonElement parent,
        string propertyName,
        List<string> problems)
    {
        if (!parent.TryGetProperty(propertyName, out JsonElement value) ||
            value.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{propertyName} is required and must be an object.");
            return default;
        }

        return value;
    }

    private static VulcanJobReadResult Invalid(string problem) =>
        VulcanJobReadResult.Failure(InvalidCode, [problem]);
}