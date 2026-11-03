// Purpose: Azure Service Bus trigger for the Vesta generation step.
// Retrieves the job state and dispatches execution to the core JobRunner.

using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Jobs;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace Company.ABC.Vesta.Functions.Messaging;

public sealed partial class GenerateStepFunction
{
    private readonly IJobStore _jobStore;
    private readonly JobRunner _jobRunner;
    private readonly ILogger<GenerateStepFunction> _logger;

    public GenerateStepFunction(
        IJobStore jobStore,
        JobRunner jobRunner,
        ILogger<GenerateStepFunction> logger)
    {
        _jobStore = jobStore;
        _jobRunner = jobRunner;
        _logger = logger;
    }

    [Function(nameof(GenerateStepFunction))]
    public async Task RunAsync(
        [ServiceBusTrigger("vesta-steps", "generate-sub", Connection = "VestaServiceBus")] StepMessage message,
        CancellationToken cancellationToken)
    {
        LogGenerationRequest(message.JobId, message.Round);

        if (message.Step != VestaStep.Generate)
        {
            LogIgnoringStep(message.Step.ToString());
            return;
        }

        var storedJob = await _jobStore.FindAsync(message.JobId, cancellationToken);
        if (storedJob is null)
        {
            LogJobNotFound(message.JobId);
            throw new InvalidOperationException($"Job {message.JobId} not found.");
        }

        await _jobRunner.ProcessStepAsync(message, storedJob.Job, cancellationToken);
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Information, Message = "Received generation request for JobId: {JobId}, Round: {Round}")]
    private partial void LogGenerationRequest(string jobId, int round);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Ignoring step {Step} in Generate function.")]
    private partial void LogIgnoringStep(string step);

    [LoggerMessage(EventId = 3, Level = LogLevel.Error, Message = "Job {JobId} not found.")]
    private partial void LogJobNotFound(string jobId);
}