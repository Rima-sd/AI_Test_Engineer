
namespace TestEngineerWorker.Utils;

public static class AppConstant
{
    // Configuration sections
    public const string BlobStorageSection = "BlobStorage";
    public const string BlobStorageAuthenticationMode =
        "BlobStorage:AuthenticationMode";
    public const string BlobStorageConnectionString =
        "BlobStorage:ConnectionString";
    public const string BlobStorageServiceUri =
        "BlobStorage:ServiceUri";
    public const string BlobStorageContainerName =
        "BlobStorage:ContainerName";

    // Authentication modes
    public const string ConnectionStringAuthentication = "ConnectionString";
    public const string ManagedIdentityAuthentication = "ManagedIdentity";

    // Project storage folders
    public const string InputFolderName = "CodeOutput";
    public const string OutputFolderName = "CodeOutputWithTest";

    // Worker execution
    public const int MaximumValidationAttempts = 5;

    // Temporary workspace
    public const string WorkerWorkspaceFolder = "TestEngineerWorker";
    public const string InputZipFileName = "input-project.zip";
    public const string ExtractedWorkspaceFolder = "workspace";

    // File extensions
    public const string CSharpProjectExtension = ".csproj";
    public const string SolutionExtension = ".sln";
    public const string SolutionXmlExtension = ".slnx";
}
