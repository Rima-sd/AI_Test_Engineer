
// Purpose: Validates required settings, resolved container names and configuration ranges together during application startup.

using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Configuration;

public sealed class OptionsValidators : IValidateOptions<VestaOptions>
{
    public ValidateOptionsResult Validate(string? name, VestaOptions options)
    {
        var errors = new List<string>();

        // Vesta Storage
        if (string.IsNullOrWhiteSpace(options.ConnectionString) &&
            string.IsNullOrWhiteSpace(options.BlobServiceUri))
        {
            errors.Add(
                "Set VestaStorage__BlobServiceUri or VestaStorage__ConnectionString.");
        }

        if (string.IsNullOrWhiteSpace(options.JobsContainer))
        {
            errors.Add("VestaStorage__JobsContainer must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.OutputContainer))
        {
            errors.Add("VestaStorage__OutputContainer is required.");
        }

        // Vulcan Storage
        if (string.IsNullOrWhiteSpace(options.VulcanJobsContainer))
        {
            errors.Add("VulcanStorage__JobsContainer must not be empty.");
        }

        if (string.IsNullOrWhiteSpace(options.VulcanOutputContainer))
        {
            errors.Add("VulcanStorage__OutputContainer must not be empty.");
        }

        // Minerva Storage
        if (string.IsNullOrWhiteSpace(options.MinervaRunsContainer))
        {
            errors.Add("MinervaStorage__RunsContainer must not be empty.");
        }

        // Service Bus
        if (string.IsNullOrWhiteSpace(options.ServiceBusConnection) &&
            string.IsNullOrWhiteSpace(options.ServiceBusFullyQualifiedNamespace))
        {
            errors.Add(
                "Set ServiceBusConnection or ServiceBusConnection__fullyQualifiedNamespace.");
        }

        if (string.IsNullOrWhiteSpace(options.ServiceBusTopicName))
        {
            errors.Add("ServiceBus__TopicName must not be empty.");
        }

        // Azure AI Foundry
        if (string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint))
        {
            errors.Add("Foundry__ProjectEndpoint is required.");
        }

        if (string.IsNullOrWhiteSpace(options.VestaAgentName))
        {
            errors.Add("Foundry__VestaAgentName is required.");
        }

        if (string.IsNullOrWhiteSpace(options.VestaAgentVersion))
        {
            errors.Add("Foundry__VestaAgentVersion is required.");
        }

        // Agent and repair limits
        if (options.AgentTimeoutSeconds is < 1 or > 800)
        {
            errors.Add("Agents__TimeoutSeconds must be between 1 and 800.");
        }

        if (options.MaxRepairRounds is < 0 or > 5)
        {
            errors.Add("Check__MaxRepairRounds must be between 0 and 5.");
        }

        // Input-processing limits
        if (options.MaxZipBytes <= 0)
        {
            errors.Add("Limits__MaxZipBytes must be positive.");
        }

        if (options.MaxZipEntries <= 0)
        {
            errors.Add("Limits__MaxZipEntries must be positive.");
        }

        if (options.MaxZipUnpackedBytes <= 0)
        {
            errors.Add("Limits__MaxZipUnpackedBytes must be positive.");
        }

        if (options.MaxAgentInputBytes <= 0)
        {
            errors.Add("Limits__MaxAgentInputBytes must be positive.");
        }

        if (options.MaxTestFileBytes <= 0)
        {
            errors.Add("Limits__MaxTestFileBytes must be positive.");
        }

        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
