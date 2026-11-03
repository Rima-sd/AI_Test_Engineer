
// Purpose: Registers validated Vesta settings, input limits and startup validation.

using Company.ABC.Vesta.Core.Inputs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Configuration;

public static class OptionsRegistration
{
    public static IServiceCollection AddVestaOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<
            IValidateOptions<VestaOptions>,
            OptionsValidators>();

        services.AddOptions<VestaOptions>()
            .Configure(options =>
            {
                options.BlobServiceUri =
                    configuration["VestaStorage:BlobServiceUri"];

                options.ConnectionString =
                    configuration["VestaStorage:ConnectionString"];

                options.JobsContainer =
                    configuration["VestaStorage:JobsContainer"]
                    ?? "vesta-jobs";

                options.OutputContainer =
                    configuration["VestaStorage:OutputContainer"];

                options.CreateContainersIfMissing =
                    configuration.GetValue(
                        "VestaStorage:CreateContainersIfMissing",
                        false);

                options.VulcanBlobServiceUri =
                    configuration["VulcanStorage:BlobServiceUri"];

                options.VulcanJobsContainer =
                    configuration["VulcanStorage:JobsContainer"]
                    ?? "vulcan-jobs";

                options.VulcanOutputContainer =
                    configuration["VulcanStorage:OutputContainer"]
                    ?? "vulcan-output";

                options.MinervaBlobServiceUri =
                    configuration["MinervaStorage:BlobServiceUri"];

                options.MinervaRunsContainer =
                    configuration["MinervaStorage:RunsContainer"]
                    ?? "runs";

                options.ServiceBusConnection =
                    configuration["ServiceBusConnection"];

                options.ServiceBusFullyQualifiedNamespace =
                    configuration[
                        "ServiceBusConnection:fullyQualifiedNamespace"];

                options.ServiceBusTopicName =
                    configuration["ServiceBus:TopicName"]
                    ?? "vesta-steps";

                options.FoundryProjectEndpoint =
                    configuration["Foundry:ProjectEndpoint"];

                options.VestaAgentName =
                    configuration["Foundry:VestaAgentName"];

                options.VestaAgentVersion =
                    configuration["Foundry:VestaAgentVersion"];

                options.AgentTimeoutSeconds =
                    configuration.GetValue(
                        "Agents:TimeoutSeconds",
                        600);

                options.MaxRepairRounds =
                    configuration.GetValue(
                        "Check:MaxRepairRounds",
                        2);

                options.MaxZipBytes =
                    configuration.GetValue(
                        "Limits:MaxZipBytes",
                        52_428_800L);

                options.MaxZipEntries =
                    configuration.GetValue(
                        "Limits:MaxZipEntries",
                        5000);

                options.MaxZipUnpackedBytes =
                    configuration.GetValue(
                        "Limits:MaxZipUnpackedBytes",
                        209_715_200L);

                options.MaxAgentInputBytes =
                    configuration.GetValue(
                        "Limits:MaxAgentInputBytes",
                        1_048_576);

                options.MaxTestFileBytes =
                    configuration.GetValue(
                        "Limits:MaxTestFileBytes",
                        262_144);
            })
            .ValidateOnStart();

        services.AddSingleton(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<VestaOptions>>()
                .Value;

            return new InputLimits(
                options.MaxZipBytes,
                options.MaxZipEntries,
                options.MaxZipUnpackedBytes,
                options.MaxAgentInputBytes,
                options.MaxTestFileBytes);
        });

        return services;
    }
}
