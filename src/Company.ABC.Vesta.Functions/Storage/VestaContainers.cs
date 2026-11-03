
// Purpose: Holds the configured container names used by Vesta and its upstream readers.

using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Storage;

public sealed class VestaContainers(IOptions<VestaOptions> options)
{
    public string Jobs => options.Value.JobsContainer;

    public string Output => options.Value.OutputContainer
        ?? throw new InvalidOperationException("VestaStorage__OutputContainer is required.");

    public string VulcanJobs => options.Value.VulcanJobsContainer;
    public string VulcanOutput => options.Value.VulcanOutputContainer;
    public string MinervaRuns => options.Value.MinervaRunsContainer;
}
