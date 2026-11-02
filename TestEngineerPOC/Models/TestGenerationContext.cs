
using System.Collections.Generic;

namespace TestEngineerPOC.Models;

public sealed class TestGenerationContext
{
    public string RequestId { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    public string TargetFilePath { get; set; } = string.Empty;

    public string TargetSourceCode { get; set; } = string.Empty;

    public string ReadmeContent { get; set; } = string.Empty;

    public TargetInventory? TargetInventory { get; set; }

    public List<DependencyInventory> Dependencies { get; set; } = new();

    public string PreviousDiagnostics { get; set; } = string.Empty;
}
