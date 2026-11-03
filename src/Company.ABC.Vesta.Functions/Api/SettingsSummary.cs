
// Purpose: Builds the safe settings and dependency readiness response.
// Never includes credentials, storage endpoints or authentication tokens.

using Company.ABC.Vesta.Functions.Configuration;

namespace Company.ABC.Vesta.Functions.Api;

public sealed record ContainerHealth(
    string Container,
    bool Exists,
    bool CanRead,
    bool? CanWrite,
    string? Error);

public sealed record SettingsSummary(
    bool Ready,
    bool VestaStorageConfigured,
    bool ServiceBusConfigured,
    bool FoundryConfigured,
    string JobsContainer,
    string OutputContainer,
    string ServiceBusTopic,
    IReadOnlyList<ContainerHealth> Containers);

public static class SettingsSummaryFactory
{
    public static SettingsSummary Create(
        VestaOptions options,
        IReadOnlyList<ContainerHealth> checks)
    {
        var ready = checks.All(check =>
            check.Exists &&
            check.CanRead &&
            (check.CanWrite != false));

        return new SettingsSummary(
            ready,
            !string.IsNullOrWhiteSpace(options.ConnectionString) ||
            !string.IsNullOrWhiteSpace(options.BlobServiceUri),
            !string.IsNullOrWhiteSpace(options.ServiceBusConnection) ||
            !string.IsNullOrWhiteSpace(options.ServiceBusFullyQualifiedNamespace),
            !string.IsNullOrWhiteSpace(options.FoundryProjectEndpoint) &&
            !string.IsNullOrWhiteSpace(options.VestaAgentName) &&
            !string.IsNullOrWhiteSpace(options.VestaAgentVersion),
            options.JobsContainer,
            options.OutputContainer ?? string.Empty,
            options.ServiceBusTopicName,
            checks);
    }
}
