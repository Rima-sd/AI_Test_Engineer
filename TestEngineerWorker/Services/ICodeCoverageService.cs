
namespace TestEngineerWorker.Services;

public interface ICodeCoverageService
{
    Task<CodeCoverageResult> AnalyzeAsync(
        string workspaceRoot,
        string targetFilePath,
        CancellationToken cancellationToken = default);
}

public sealed record CodeCoverageResult(
    double LineCoveragePercent,
    double BranchCoveragePercent,
    bool LineCoveragePassed,
    bool BranchCoveragePassed,
    string Diagnostics);
