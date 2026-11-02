/// <summary>
/// Blob Storage configuration shared by local development and Azure-hosted
/// Worker execution.
/// 
/// StorageAccount contains either:
/// - A connection string for local development.
/// - An HTTPS Blob Storage endpoint for Azure-hosted environments.
/// 
/// ContainerName identifies the container containing project ZIP files
/// and validation request/result files.
/// </summary>
/// 
namespace TestEngineerWorker.Options;

public sealed class BlobStorageOptions
{
    /// <summary>
    /// Storage account connection string for local development,
    /// or Blob Storage service endpoint for Azure-hosted environments.
    /// </summary>
    public string StorageAccount { get; set; } = string.Empty;

    /// <summary>
    /// Blob container containing input and output project files.
    /// </summary>
    public string ContainerName { get; set; } = string.Empty;
}
