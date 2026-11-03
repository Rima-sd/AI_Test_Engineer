// Purpose: Holds all Vesta configuration values and documented defaults.
namespace Company.ABC.Vesta.Functions.Configuration;

public sealed class VestaOptions
{
    public const string SectionName = "VestaStorage";
    public string? BlobServiceUri { get; set; }
    public string? ConnectionString { get; set; }
    public string JobsContainer { get; set; } = "vesta-jobs";
    public string? OutputContainer { get; set; }
    public bool CreateContainersIfMissing { get; set; }
    public string? VulcanBlobServiceUri { get; set; }
    public string VulcanJobsContainer { get; set; } = "vulcan-jobs";
    public string VulcanOutputContainer { get; set; } = "vulcan-output";
    public string? MinervaBlobServiceUri { get; set; }
    public string MinervaRunsContainer { get; set; } = "runs";
    public string? ServiceBusConnection { get; set; }
    public string? ServiceBusFullyQualifiedNamespace { get; set; }
    public string ServiceBusTopicName { get; set; } = "vesta-steps";
    public string? FoundryProjectEndpoint { get; set; }
    public string? VestaAgentName { get; set; }
    public string? VestaAgentVersion { get; set; }
    public int AgentTimeoutSeconds { get; set; } = 600;
    public int MaxRepairRounds { get; set; } = 2;
    public long MaxZipBytes { get; set; } = 52_428_800;
    public int MaxZipEntries { get; set; } = 5000;
    public long MaxZipUnpackedBytes { get; set; } = 209_715_200;
    public int MaxAgentInputBytes { get; set; } = 1_048_576;
    public int MaxTestFileBytes { get; set; } = 262_144;
}
