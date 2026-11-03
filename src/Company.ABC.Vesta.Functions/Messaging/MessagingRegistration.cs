// Purpose: Registers the singleton Service Bus client and Vesta step publisher.
// Supports local connection strings and managed identity authentication.

using Azure.Identity;
using Azure.Messaging.ServiceBus;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Functions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Company.ABC.Vesta.Functions.Messaging;

public static class MessagingRegistration
{
    public static IServiceCollection AddVestaMessaging(
        this IServiceCollection services)
    {
        services.AddSingleton(provider =>
        {
            var options = provider
                .GetRequiredService<IOptions<VestaOptions>>()
                .Value;

            return CreateClient(options);
        });

        services.AddSingleton<IStepSender, StepPublisher>();

        return services;
    }

    private static ServiceBusClient CreateClient(VestaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ServiceBusConnection))
        {
            return new ServiceBusClient(options.ServiceBusConnection);
        }

        if (string.IsNullOrWhiteSpace(
                options.ServiceBusFullyQualifiedNamespace))
        {
            throw new InvalidOperationException(
                "Service Bus namespace configuration is missing.");
        }

        return new ServiceBusClient(
            options.ServiceBusFullyQualifiedNamespace,
            new DefaultAzureCredential());
    }
}