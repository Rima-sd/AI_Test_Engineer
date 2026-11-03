// Purpose: Configures the Vesta Azure Functions isolated worker host.
// Registers configuration, storage, messaging, Application Insights, agents, and system time.
// Additional application services will be registered in subsequent build steps.

using Company.ABC.Vesta.Core.Agents;
using Company.ABC.Vesta.Core.Inputs;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Foundry;
using Company.ABC.Vesta.Foundry.Agents;
using Company.ABC.Vesta.Foundry.Configuration;
using Company.ABC.Vesta.Functions.Configuration;
using Company.ABC.Vesta.Functions.Messaging;
using Company.ABC.Vesta.Functions.Storage;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

// Typed configuration and startup validation.
builder.Services.AddVestaOptions(builder.Configuration);

// Azure Storage and Service Bus.
builder.Services.AddVestaStorage();
builder.Services.AddVestaMessaging();

// Azure AI Foundry Transport
builder.Services.AddSingleton<IAgentTransport>(sp =>
{
    var vestaOptions = sp.GetRequiredService<IOptions<VestaOptions>>().Value;

    var foundryOptions = new FoundryOptions
    {
        ProjectEndpoint = vestaOptions.FoundryProjectEndpoint!,
        AgentId = vestaOptions.VestaAgentName!,
        TimeoutSeconds = vestaOptions.AgentTimeoutSeconds
    };

    return new FoundryAgentTransport(foundryOptions);
});

// Core Domain Services
builder.Services.AddSingleton(new InputLimits(
    maxZipBytes: 50 * 1024 * 1024,          // 50 MB
    maxZipEntries: 2000,                    // 2000 files max
    maxZipUnpackedBytes: 250 * 1024 * 1024, // 250 MB unpacked
    maxAgentInputBytes: 2 * 1024 * 1024,    // 2 MB prompt limit
    maxTestFileBytes: 5 * 1024 * 1024       // 5 MB max output
));
builder.Services.AddSingleton<JobRunner>();

// Application Insights.
builder.Services.AddApplicationInsightsTelemetryWorkerService();
builder.Services.ConfigureFunctionsApplicationInsights();

// Shared system time provider.
builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);

await builder.Build().RunAsync();