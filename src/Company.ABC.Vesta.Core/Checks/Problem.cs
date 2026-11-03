// Purpose: Defines problem entities and categories found during test file validation.

namespace Company.ABC.Vesta.Core.Checks;

public sealed record Problem(
    string Code,
    ProblemKind Kind,
    int Line,
    int Column,
    string Message);

public enum ProblemKind
{
    Safety,
    Quality,
    Warning
}