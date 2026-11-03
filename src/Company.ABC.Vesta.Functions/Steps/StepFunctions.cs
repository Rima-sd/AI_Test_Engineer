// Purpose: Azure Functions Service Bus trigger for Vesta pipeline steps.

using Azure.Messaging.ServiceBus;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Steps;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Company.ABC.Vesta.Functions.Steps;

public static partial class StepFunctionsLogging
{
    [LoggerMessage(EventId = 3001, Level = LogLevel.Warning, Message = "Misrouted message received on subscription {ExpectedStep}")]
    public static partial void LogMisroutedMessage(ILogger logger, string expectedStep);
}

public sealed class StepFunctions
{
    private readonly JobStepRunner _stepRunner;
    private readonly ILogger<StepFunctions> _logger;

    public StepFunctions(JobStepRunner stepRunner, ILogger<StepFunctions> logger)
    {
        _stepRunner = stepRunner;
        _logger = logger;
    }

    [Function("ReceiveStep")]
    public Task RunReceiveAsync(
        [ServiceBusTrigger("vesta-steps", "receive", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
        => ProcessMessageAsync(message, VestaStep.Receive, cancellationToken);

    [Function("GenerateStep")]
    public Task RunGenerateAsync(
        [ServiceBusTrigger("vesta-steps", "generate", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
        => ProcessMessageAsync(message, VestaStep.Generate, cancellationToken);

    [Function("CheckStep")]
    public Task RunCheckAsync(
        [ServiceBusTrigger("vesta-steps", "check", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
        => ProcessMessageAsync(message, VestaStep.Check, cancellationToken);

    [Function("PackageStep")]
    public Task RunPackageAsync(
        [ServiceBusTrigger("vesta-steps", "package", Connection = "ServiceBusConnection")] ServiceBusReceivedMessage message,
        CancellationToken cancellationToken)
        => ProcessMessageAsync(message, VestaStep.Package, cancellationToken);

    private async Task ProcessMessageAsync(ServiceBusReceivedMessage message, VestaStep expectedStep, CancellationToken cancellationToken)
    {
        var stepProp = message.ApplicationProperties.TryGetValue("step", out var stepVal) ? stepVal?.ToString() : null;
        if (!string.Equals(stepProp, expectedStep.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            StepFunctionsLogging.LogMisroutedMessage(_logger, expectedStep.ToString());
            return;
        }

        var stepMessage = JsonSerializer.Deserialize<StepMessage>(message.Body.ToString(), ContractJson.Options)
            ?? throw new InvalidOperationException("Failed to deserialize step message.");

        await _stepRunner.ProcessMessageAsync(stepMessage, cancellationToken);
    }
}