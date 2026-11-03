// Purpose: Loads and validates Vesta documents against embedded JSON schemas.
// Builds each schema once and collects all validation errors when evaluating documents.

using System.Reflection;
using System.Text.Json;
using Json.Schema;

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Validates Vesta documents against their corresponding JSON Schema contracts.
/// </summary>
public static class ContractValidator
{
    private static readonly Dictionary<ContractKind, JsonSchema> Schemas = LoadSchemas();

    private static readonly EvaluationOptions EvaluationOptions = new()
    {
        OutputFormat = OutputFormat.Hierarchical,
        RequireFormatValidation = true
    };

    /// <summary>
    /// Validates a raw JSON string against the specified Vesta contract.
    /// </summary>
    /// <param name="contractKind">The contract to validate against.</param>
    /// <param name="json">The JSON text to validate.</param>
    public static void Validate(
        ContractKind contractKind,
        string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using JsonDocument document = JsonDocument.Parse(json);
        Validate(contractKind, document.RootElement);
    }

    /// <summary>
    /// Validates a JSON document against the specified Vesta contract.
    /// </summary>
    /// <param name="contractKind">The contract to validate against.</param>
    /// <param name="document">The JSON document to validate.</param>
    public static void Validate(
        ContractKind contractKind,
        JsonElement document)
    {
        if (!Schemas.TryGetValue(contractKind, out JsonSchema? schema))
        {
            throw new ArgumentOutOfRangeException(
                nameof(contractKind),
                contractKind,
                "Unknown Vesta contract kind.");
        }

        EvaluationResults results = schema.Evaluate(
            document,
            EvaluationOptions);

        if (results.IsValid)
        {
            return;
        }

        List<string> problems = [];

        CollectProblems(results, problems);

        if (problems.Count == 0)
        {
            problems.Add("The JSON document does not satisfy the schema.");
        }

        throw new ContractViolationException(
            contractKind,
            problems);
    }

    private static Dictionary<ContractKind, JsonSchema> LoadSchemas()
    {
        Assembly assembly = typeof(ContractValidator).Assembly;

        Dictionary<ContractKind, string> resourceNames = new()
        {
            [ContractKind.Job] = "Schemas.job.schema.json",
            [ContractKind.StepMessage] = "Schemas.step-message.schema.json",
            [ContractKind.AuditEntry] = "Schemas.audit-entry.schema.json",
            [ContractKind.AgentRequest] = "Schemas.agent-request.schema.json",
            [ContractKind.AgentReply] = "Schemas.agent-reply.schema.json"
        };

        Dictionary<ContractKind, JsonSchema> schemas = [];

        foreach (KeyValuePair<ContractKind, string> resource in resourceNames)
        {
            using Stream stream = assembly.GetManifestResourceStream(
                resource.Value)
                ?? throw new InvalidOperationException(
                    $"Embedded schema resource not found: {resource.Value}");

            using JsonDocument schemaDocument = JsonDocument.Parse(stream);

            // Clone the root element so the schema remains usable
            // after the JsonDocument is disposed.
            JsonElement schemaRoot = schemaDocument.RootElement.Clone();

            JsonSchema schema = JsonSchema.Build(
                schemaRoot,
                new BuildOptions
                {
                    Dialect = Dialect.Draft202012
                });

            schemas.Add(resource.Key, schema);
        }

        return schemas;
    }

    private static void CollectProblems(
        EvaluationResults result,
        ICollection<string> problems)
    {
        // Ignore successful evaluations and their unsuccessful alternative branches.
        if (result.IsValid)
        {
            return;
        }

        if (result.Errors is { Count: > 0 })
        {
            foreach (KeyValuePair<string, string> error in result.Errors)
            {
                problems.Add(
                    $"{result.InstanceLocation}: {error.Key}: {error.Value}");
            }
        }

        if (result.Details is null)
        {
            return;
        }

        foreach (EvaluationResults detail in result.Details)
        {
            CollectProblems(detail, problems);
        }
    }
}