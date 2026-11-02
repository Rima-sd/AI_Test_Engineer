
/// <summary>
/// Purpose:
/// Submits one validation attempt to AKS as a Kubernetes Job.
///
/// Usage:
/// The Function App creates a validation request in Blob Storage,
/// then calls this service to start the Worker container.
///
/// Configuration:
/// - Aks__ApiServerUrl
/// - Aks__CertificateAuthorityData
/// - Aks__Namespace
/// - Aks__WorkerImage
/// - Aks__ServiceAccountName
/// - StorageAccount
/// - StorageAccount__ContainerName
///
/// Authentication:
/// Uses the Function App managed identity to authenticate to the
/// AKS Kubernetes API through Microsoft Entra ID.
/// The Worker uses AKS Workload Identity to access Blob Storage.
/// </summary>

using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure.Core;
using Azure.Identity;
using k8s;
using k8s.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace TestEngineerPOC.Services;

public sealed class AksValidationJobService
{
    private const string AksServerApplicationId =
        "6dae42f8-4368-4678-94ff-3960e28e3630";

    private readonly IConfiguration _configuration;
    private readonly ILogger<AksValidationJobService> _logger;

    public AksValidationJobService(
        IConfiguration configuration,
        ILogger<AksValidationJobService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<string> SubmitValidationJobAsync(
        string requestId,
        int attemptNumber,
        string containerName,
        string requestBlobName,
        string resultBlobName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(requestId))
            throw new ArgumentException("Request ID is required.");

        if (attemptNumber is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(attemptNumber));

        ValidateBlobName(requestBlobName, "ValidationRequests/");
        ValidateBlobName(resultBlobName, "ValidationResults/");

        var apiServerUrl = GetRequiredSetting("Aks__ApiServerUrl");
        var caCertificateData =
            GetRequiredSetting("Aks__CertificateAuthorityData");
        var namespaceName = GetRequiredSetting("Aks__Namespace");
        var workerImage = GetRequiredSetting("Aks__WorkerImage");
        var storageAccount = GetRequiredSetting("StorageAccount");

        var serviceAccountName =
            GetRequiredSetting("Aks__ServiceAccountName");

        if (!Uri.TryCreate(
                apiServerUrl,
                UriKind.Absolute,
                out var apiUri) ||
            apiUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "Aks__ApiServerUrl must be a valid HTTPS URL.");
        }

        if (!Uri.TryCreate(
                storageAccount,
                UriKind.Absolute,
                out var storageUri) ||
            storageUri.Scheme != Uri.UriSchemeHttps)
        {
            throw new InvalidOperationException(
                "StorageAccount must be a valid HTTPS Blob Storage URL.");
        }

        var jobName = CreateJobName(requestId, attemptNumber);

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=AksJobSubmissionStarted] " +
            "JobName={JobName}; Namespace={Namespace}; Attempt={Attempt}",
            requestId,
            jobName,
            namespaceName,
            attemptNumber);

        // Acquire a Microsoft Entra token for the AKS Kubernetes API.
        TokenCredential credential = new DefaultAzureCredential();

        var accessToken = await credential.GetTokenAsync(
            new TokenRequestContext(
                new[]
                {
                    $"{AksServerApplicationId}/.default"
                }),
            cancellationToken);

        // Configure Kubernetes API authentication and TLS validation.
        var caPem = Encoding.UTF8.GetString(
            Convert.FromBase64String(caCertificateData));

        using var caCertificate =
            X509Certificate2.CreateFromPem(caPem);

        var clientConfiguration = new KubernetesClientConfiguration
        {
            Host = apiUri.ToString(),
            AccessToken = accessToken.Token,
            SslCaCerts = new X509Certificate2Collection
            {
                caCertificate
            }
        };

        using var kubernetesClient =
            new Kubernetes(clientConfiguration);

        // One Kubernetes Job performs exactly one validation attempt.
        var job = new V1Job
        {
            ApiVersion = "batch/v1",
            Kind = "Job",

            Metadata = new V1ObjectMeta
            {
                Name = jobName,
                NamespaceProperty = namespaceName,
                Labels = new Dictionary<string, string>
                {
                    ["app"] = "ai-test-engineer",
                    ["request-id"] = CreateLabelValue(requestId),
                    ["attempt"] = attemptNumber.ToString()
                }
            },

            Spec = new V1JobSpec
            {
                BackoffLimit = 0,
                TtlSecondsAfterFinished = 3600,
                ActiveDeadlineSeconds = 1800,

                Template = new V1PodTemplateSpec
                {
                    Metadata = new V1ObjectMeta
                    {
                        Labels = new Dictionary<string, string>
                        {
                            ["app"] = "ai-test-engineer",
                            ["request-id"] = CreateLabelValue(requestId),

                            // Required for AKS Workload Identity.
                            ["azure.workload.identity/use"] = "true"
                        }
                    },

                    Spec = new V1PodSpec
                    {
                        RestartPolicy = "Never",
                        ServiceAccountName = serviceAccountName,

                        Containers = new List<V1Container>
                        {
                            new()
                            {
                                Name = "test-engineer-worker",
                                Image = workerImage,
                                ImagePullPolicy = "IfNotPresent",

                                Env = new List<V1EnvVar>
                                {
                                    new()
                                    {
                                        Name = "DOTNET_ENVIRONMENT",
                                        Value = "Production"
                                    },
                                    new()
                                    {
                                        Name = "Worker__RequestBlobName",
                                        Value = requestBlobName
                                    },
                                    new()
                                    {
                                        Name = "Worker__ResultBlobName",
                                        Value = resultBlobName
                                    },
                                    new()
                                    {
                                        Name = "StorageAccount",
                                        Value = storageUri.ToString()
                                    },
                                    new()
                                    {
                                        Name = "StorageAccount__ContainerName",
                                        Value = containerName
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };

        var createdJob =
            await kubernetesClient.BatchV1.CreateNamespacedJobAsync(
                job,
                namespaceName,
                cancellationToken: cancellationToken);

        _logger.LogInformation(
            "[RequestId={RequestId}] [Stage=AksJobSubmitted] " +
            "JobName={JobName}; Namespace={Namespace}; " +
            "Attempt={Attempt}; KubernetesJobName={KubernetesJobName}",
            requestId,
            jobName,
            namespaceName,
            attemptNumber,
            createdJob.Metadata?.Name);

        return createdJob.Metadata?.Name
            ?? throw new InvalidOperationException(
                "AKS did not return the created Job name.");
    }

    private string GetRequiredSetting(string name)
    {
        return _configuration[name]
            ?? throw new InvalidOperationException(
                $"Required configuration '{name}' is missing.");
    }

    private static string CreateJobName(
        string requestId,
        int attemptNumber)
    {
        var normalized = new string(
            requestId.ToLowerInvariant()
                .Select(character =>
                    char.IsAsciiLetterOrDigit(character)
                        ? character
                        : '-')
                .ToArray())
            .Trim('-');

        if (normalized.Length > 35)
            normalized = normalized[..35].TrimEnd('-');

        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "request";

        return $"test-validation-{normalized}-a{attemptNumber}";
    }

    private static string CreateLabelValue(string value)
    {
        var normalized = new string(
            value.ToLowerInvariant()
                .Select(character =>
                    char.IsAsciiLetterOrDigit(character) ||
                    character is '-' or '_' or '.'
                        ? character
                        : '-')
                .ToArray())
            .Trim('-', '_', '.');

        return normalized.Length > 63
            ? normalized[..63].TrimEnd('-', '_', '.')
            : normalized;
    }

    private static void ValidateBlobName(
        string blobName,
        string requiredPrefix)
    {
        if (string.IsNullOrWhiteSpace(blobName) ||
            !blobName.StartsWith(
                requiredPrefix,
                StringComparison.Ordinal) ||
            blobName.Contains('\\') ||
            blobName.Contains(':') ||
            blobName.Split('/').Any(
                segment => segment is "." or ".." or ""))
        {
            throw new InvalidOperationException(
                $"Invalid Blob path. Expected prefix: {requiredPrefix}");
        }
    }
}
