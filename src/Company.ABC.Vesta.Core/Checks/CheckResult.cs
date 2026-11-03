// Purpose: Encapsulates the results of test file checks and compilation.

namespace Company.ABC.Vesta.Core.Checks;

public sealed record CheckResult(
    int Round,
    bool Passed,
    bool Safe,
    IReadOnlyList<Problem> Problems,
    IReadOnlyDictionary<string, IReadOnlyList<string>> RowsCovered);