// Purpose: Publishes validated step messages with the step application property.
using Azure.Messaging.ServiceBus;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Extensions.Options;
namespace Company.ABC.Vesta.Functions.Messaging;

public sealed class StepPublisher(ServiceBusClient client, IOptions<VestaOptions> options) : IStepSender
{
    public async Task PublishAsync(StepMessage message, CancellationToken cancellationToken = default)
    {
        var sender = client.CreateSender(options.Value.ServiceBusTopicName);
        try
        {
            var body = BinaryData.FromObjectAsJson(message, ContractJson.Options);
            var busMessage = new ServiceBusMessage(body) { ContentType = "application/json" };
            busMessage.ApplicationProperties["step"] = message.Step.ToString();
            await sender.SendMessageAsync(busMessage, cancellationToken).ConfigureAwait(false);
        }
        finally { await sender.DisposeAsync().ConfigureAwait(false); }
    }
}
