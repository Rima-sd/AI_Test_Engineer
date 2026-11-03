
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Azure.Functions.Worker.OpenTelemetry;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using TestEngineerPOC.Services;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services.AddOpenTelemetry()
    .UseFunctionsWorkerDefaults()
    .UseAzureMonitorExporter();

// STAGE 8: Register target inventory service.
builder.Services.AddSingleton<TargetInventoryService>();

// STAGE 9: Register dependency discovery service.
builder.Services.AddSingleton<TargetDependencyService>();

//STAGE 10: Register discovered dependency service.
builder.Services.AddSingleton<DependencyInventoryService>();

//STAGE 11: Register structured context for unit-test generation
builder.Services.AddSingleton<TestGenerationContextService>();

// STAGE 12: Register unit-test generation service.
builder.Services.AddSingleton<UnitTestGenerationService>();

// STAGE 13: Register generated test-file parsing and saving service.
builder.Services.AddSingleton<GeneratedTestFileService>();

builder.Services.AddSingleton<FoundryAgentService>();

builder.Services.AddSingleton<ProjectPackagingService>();

// STAGE 14: Register Blob Storage client factory.
builder.Services.AddSingleton<BlobStorageClientFactory>();

// STAGE 15: Register AKS validation job service.
builder.Services.AddSingleton<AksValidationJobService>();



builder.Build().Run();
