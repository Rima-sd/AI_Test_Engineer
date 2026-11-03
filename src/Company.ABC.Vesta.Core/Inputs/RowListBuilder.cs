// Purpose: Builds the deterministic row list that generated tests must cover.

namespace Company.ABC.Vesta.Core.Inputs;

public sealed record MappingRowListItem(
    string RowId,
    string ScopeId,
    string TargetPath,
    string RuleType);

public static class RowListBuilder
{
    public static IReadOnlyList<MappingRowListItem> Build(
        ParsedMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);

        return mapping.Rows
            .Select(row => new MappingRowListItem(
                row.RowId,
                row.ScopeId,
                row.TargetPath,
                row.RuleType))
            .OrderBy(row => row.RowId, StringComparer.Ordinal)
            .ToList();
    }
}