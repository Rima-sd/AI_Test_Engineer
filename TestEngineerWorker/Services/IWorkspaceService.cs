
namespace TestEngineerWorker.Services;

public interface IWorkspaceService
{
    void ExtractZipSafely(
        string zipFilePath,
        string workspaceRoot);
}
