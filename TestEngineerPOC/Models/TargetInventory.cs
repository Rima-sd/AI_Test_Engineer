using System.Collections.Generic;

namespace TestEngineerPOC.Models;

/// <summary>
/// Describes the initial inventory extracted from a selected C# source file.
/// This is metadata only; it does not contain or modify application code.
/// </summary>
public sealed class TargetInventory
{
    // Tracks this inventory against the incoming Service Bus request.
    public string RequestId { get; init; } = string.Empty;

    // Stores the selected source path relative to the extracted project.
    public string TargetPath { get; init; } = string.Empty;

    // Stores the source content length in characters.
    public int SourceLength { get; init; }

    // STAGE 8: Store namespace, type, method, and using information.
    public List<string> Namespaces { get; init; } = new();
    public List<string> TypeNames { get; init; } = new();
    public List<string> MethodNames { get; init; } = new();
    public List<string> UsingNamespaces { get; init; } = new();
}
