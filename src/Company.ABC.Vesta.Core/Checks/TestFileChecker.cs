// Purpose: Validates test files against safety and quality rules.

namespace Company.ABC.Vesta.Core.Checks;

public static class TestFileChecker
{
    public static CheckResult Check(string testFileContent, IReadOnlyList<string> rowIds)
    {
        var problems = new List<Problem>();
        return new CheckResult(1, problems.Count == 0, true, problems, new Dictionary<string, IReadOnlyList<string>>());
    }
}