
// Purpose: Represents the Vulcan job fields consumed by Vesta during input processing.

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record VulcanJob(
    string JobId,
    string MappingId,
    string OriginKind,
    string? OriginRunId,
    int? OriginPackageVersion,
    string ProjectFullName,
    string State,
    int Generation,
    string? OutputZipPath,
    bool OutputCompiled,
    int OutputGeneration);
