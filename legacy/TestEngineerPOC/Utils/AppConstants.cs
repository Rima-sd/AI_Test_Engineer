
/*
 * Purpose:
 * Centralized application constants for configuration keys,
 * Blob Storage folder paths, and retry settings used by the
 * AI Test Engineer Azure Function.
 */

namespace TestEngineerPOC.Utils;

public static class AppConstants
{
    // Blob Storage configuration keys
    public const string StorageAccount = "StorageAccount";
    public const string StorageAccountContainerName =
        "StorageAccount:ContainerName";

    // Service Bus configuration keys
    public const string ServiceBusConnection = "ServiceBusConnection";
    public const string ServiceBusTopicName = "ServiceBusTopicName";
    public const string ServiceBusSubscriptionName =
        "ServiceBusSubscriptionName";

    // Foundry environment variable names
    // Used with Environment.GetEnvironmentVariable()
    public const string FoundryProjectEndpoint =
        "Foundry__ProjectEndpoint";
    public const string FoundryAgentName =
        "Foundry__AgentName";
    public const string FoundryAgentVersion =
        "Foundry__AgentVersion";

    // AKS configuration keys
    // Used with IConfiguration
    public const string AksApiServerUrl =
        "Aks:ApiServerUrl";
    public const string AksCertificateAuthorityData =
        "Aks:CertificateAuthorityData";
    public const string AksNamespace =
        "Aks:Namespace";
    public const string AksWorkerImage =
        "Aks:WorkerImage";
    public const string AksServiceAccountName =
        "Aks:ServiceAccountName";

    // Blob Storage folder prefixes
    public const string InputFolder = "CodeOutput/";
    public const string OutputFolder = "CodeOutputWithTest/";
    public const string ValidationRequestsFolder =
        "ValidationRequests/";
    public const string ValidationResultsFolder =
        "ValidationResults/";

    // Retry configuration
    public const int MaximumAttempts = 5;
}
