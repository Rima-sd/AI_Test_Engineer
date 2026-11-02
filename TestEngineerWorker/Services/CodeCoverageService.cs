
/*
 * Purpose:
 * Reads Cobertura reports and calculates coverage for only the target
 * production source file selected from the project's README.
 *
 * It deliberately does not use the report's project-wide totals.
 */


using System.Globalization;
using System.Xml.Linq;

namespace TestEngineerWorker.Services;

public sealed class CodeCoverageService : ICodeCoverageService
{
    private const double RequiredCoveragePercent = 100.0;

    public Task<CodeCoverageResult> AnalyzeAsync(
        string workspaceRoot,
        string targetFilePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFilePath);
        cancellationToken.ThrowIfCancellationRequested();

        var root = Path.GetFullPath(workspaceRoot);

        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException(
                $"Coverage workspace was not found: {root}");
        }

        var resolvedTargetPath = ResolveTargetPath(root, targetFilePath);

        if (!File.Exists(resolvedTargetPath))
        {
            throw new FileNotFoundException(
                $"README target source file was not found: {targetFilePath}",
                resolvedTargetPath);
        }

        var targetRelativePath = NormalizeRelativePath(
            Path.GetRelativePath(root, resolvedTargetPath));

        var coverageFiles = Directory
            .EnumerateFiles(
                root,
                "coverage.cobertura.xml",
                SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (coverageFiles.Length == 0)
        {
            throw new FileNotFoundException(
                "No coverage.cobertura.xml report was found.",
                root);
        }

        var matches = new List<TargetCoverageSnapshot>();

        var diagnostics = new List<string>
        {
            $"Coverage target: {targetRelativePath}",
            "Coverage scope: README-selected target file only."
        };

        foreach (var coverageFile in coverageFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var document = XDocument.Load(coverageFile);
            var reportRoot = document.Root;

            if (reportRoot is null ||
                reportRoot.Name.LocalName != "coverage")
            {
                diagnostics.Add(
                    $"Skipped invalid Cobertura report: {coverageFile}");

                continue;
            }

            var matchingClasses = reportRoot
                .Descendants()
                .Where(element => element.Name.LocalName == "class")
                .Where(element => IsTargetClass(
                    element,
                    reportRoot,
                    root,
                    targetRelativePath))
                .ToArray();

            if (matchingClasses.Length == 0)
            {
                diagnostics.Add(
                    $"Target not present in report: {coverageFile}");

                continue;
            }

            if (matchingClasses.Length > 1)
            {
                throw new InvalidDataException(
                    $"More than one Cobertura class entry matched " +
                    $"'{targetRelativePath}' in report '{coverageFile}'.");
            }

            var snapshot = ReadClassCoverage(
                 matchingClasses[0],
                 coverageFile,
                 targetRelativePath,
                 resolvedTargetPath);

            matches.Add(snapshot);
        }

        if (matches.Count == 0)
        {
            throw new InvalidDataException(
                $"The target file '{targetRelativePath}' was not found " +
                "in any Cobertura report. Project-wide totals cannot " +
                "be used as a substitute.");
        }

        // Several test projects may produce reports for the same target.
        // Select the most complete instrumentation rather than adding
        // duplicate line and branch counters together.
        var selected = matches
            .OrderByDescending(item => item.LineCount + item.BranchCount)
            .ThenByDescending(item => item.LineCount)
            .ThenByDescending(item => item.BranchCount)
            .First();

        var equallyComplete = matches
            .Where(item =>
                item.LineCount == selected.LineCount &&
                item.BranchCount == selected.BranchCount)
            .ToArray();

        if (equallyComplete.Any(item =>
                item.LinesCovered != selected.LinesCovered ||
                item.BranchesCovered != selected.BranchesCovered))
        {
            throw new InvalidDataException(
                $"Multiple Cobertura reports contain conflicting coverage " +
                $"results for '{targetRelativePath}' with the same " +
                "instrumentation size.");
        }

        var linePercent = selected.LineCount == 0
            ? 0.0
            : selected.LinesCovered * 100.0 / selected.LineCount;

        // A file with no conditional branches has no branch paths left
        // uncovered. Report N/A and treat branch coverage as satisfied.
        var branchPercent = selected.BranchCount == 0
            ? 100.0
            : selected.BranchesCovered * 100.0 / selected.BranchCount;

        var linePassed =
            selected.LineCount > 0 &&
            linePercent >= RequiredCoveragePercent;

        var branchPassed =
            selected.BranchCount == 0 ||
            branchPercent >= RequiredCoveragePercent;

        diagnostics.Add($"Selected report: {selected.ReportPath}");

        foreach (var other in matches.Where(item => item != selected))
        {
            diagnostics.Add(
                $"Ignored duplicate/less complete report: {other.ReportPath}");
        }

        diagnostics.Add(
            $"Target line coverage: {linePercent:F2}% " +
            $"({selected.LinesCovered}/{selected.LineCount})");

        diagnostics.Add(
            selected.BranchCount == 0
                ? "Target branch coverage: N/A (no branch conditions; treated as 100%)."
                : $"Target branch coverage: {branchPercent:F2}% " +
                  $"({selected.BranchesCovered}/{selected.BranchCount})");

        diagnostics.Add($"Target line coverage passed: {linePassed}");
        diagnostics.Add($"Target branch coverage passed: {branchPassed}");

        // Detailed branch diagnostics for test-generation feedback.
        diagnostics.Add("Branch coverage details:");

        if (selected.BranchCount == 0)
        {
            diagnostics.Add("No branch conditions were found.");
        }
        else
        {
            foreach (var branchDiagnostic in selected.BranchDiagnostics)
            {
                diagnostics.Add(branchDiagnostic);
            }
        }

        return Task.FromResult(new CodeCoverageResult(
            linePercent,
            branchPercent,
            linePassed,
            branchPassed,
            string.Join(Environment.NewLine, diagnostics)));
    }

    private static string ResolveTargetPath(
        string workspaceRoot,
        string targetFilePath)
    {
        var candidate = targetFilePath.Trim()
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        var fullPath = Path.IsPathRooted(candidate)
            ? Path.GetFullPath(candidate)
            : Path.GetFullPath(
                Path.Combine(workspaceRoot, candidate));

        var relative = Path.GetRelativePath(workspaceRoot, fullPath);

        if (relative == ".." ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal) ||
            Path.IsPathRooted(relative))
        {
            throw new InvalidDataException(
                $"Target file must be inside the workspace: {targetFilePath}");
        }

        return fullPath;
    }

    private static bool IsTargetClass(
        XElement classElement,
        XElement reportRoot,
        string workspaceRoot,
        string targetRelativePath)
    {
        var filename =
            (string?)classElement.Attribute("filename");

        if (string.IsNullOrWhiteSpace(filename))
        {
            return false;
        }

        var normalizedFilename = NormalizeReportPath(
            filename,
            workspaceRoot);

        if (PathEquals(normalizedFilename, targetRelativePath))
        {
            return true;
        }

        // Some Cobertura reports store paths relative to <source>.
        var sources = reportRoot
            .Descendants()
            .Where(element => element.Name.LocalName == "source")
            .Select(element => element.Value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value));

        foreach (var source in sources)
        {
            var sourcePath = Path.IsPathRooted(source)
                ? Path.GetFullPath(source)
                : Path.GetFullPath(
                    Path.Combine(workspaceRoot, source));

            var combined = Path.GetFullPath(
                Path.Combine(
                    sourcePath,
                    filename.Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar)));

            var relative = Path.GetRelativePath(
                workspaceRoot,
                combined);

            if (relative != ".." &&
                !relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal) &&
                PathEquals(relative, targetRelativePath))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeReportPath(
        string filename,
        string workspaceRoot)
    {
        if (Path.IsPathRooted(filename))
        {
            var full = Path.GetFullPath(filename);
            var relative = Path.GetRelativePath(
                workspaceRoot,
                full);

            if (relative != ".." &&
                !relative.StartsWith(
                    ".." + Path.DirectorySeparatorChar,
                    StringComparison.Ordinal))
            {
                return NormalizeRelativePath(relative);
            }
        }

        return NormalizeRelativePath(filename);
    }

    private static string NormalizeRelativePath(string path)
    {
        var normalized = path.Trim().Replace('\\', '/');

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized.TrimStart('/');
    }

    private static bool PathEquals(string left, string right)
    {
        return string.Equals(
            NormalizeRelativePath(left),
            NormalizeRelativePath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static TargetCoverageSnapshot ReadClassCoverage(
                XElement classElement,
                string reportPath,
                string targetRelativePath,
                string targetSourcePath)
    {
        var linesElement = classElement.Elements()
            .FirstOrDefault(element =>
                element.Name.LocalName == "lines");

        var lineElements = linesElement?.Elements()
            .Where(element => element.Name.LocalName == "line")
            .ToArray() ?? [];

        var lineCount = lineElements.Length;

        var linesCovered = lineElements.Count(line =>
            int.TryParse(
                (string?)line.Attribute("hits"),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var hits) &&
            hits > 0);

        var branchCount = 0;
        var branchesCovered = 0;

        var branchDiagnostics = new List<string>();

        foreach (var line in lineElements)
        {
            var isBranch = string.Equals(
                (string?)line.Attribute("branch"),
                "true",
                StringComparison.OrdinalIgnoreCase);

            if (!isBranch)
            {
                continue;
            }

            var conditionCoverage =
                (string?)line.Attribute("condition-coverage");

            if (!TryReadConditionCounts(
                    conditionCoverage,
                    out var covered,
                    out var total))
            {
                throw new InvalidDataException(
                    $"Branch line in '{targetRelativePath}' has no readable " +
                    $"condition data in report '{reportPath}'.");
            }

            branchCount += total;
            branchesCovered += covered;

            var lineNumber =
                (string?)line.Attribute("number") ?? "unknown";

            var linePercent = total == 0
                ? 100.0
                : covered * 100.0 / total;

            var uncovered = total - covered;

            branchDiagnostics.Add(
                $"Branch line {lineNumber}: {covered}/{total} outcomes covered " +
                $"({linePercent:F2}%). " +
                $"Uncovered outcomes: {uncovered}.");

            if (uncovered > 0 &&
                int.TryParse(
                    lineNumber,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out var sourceLineNumber))
            {
                var sourceContext = ReadSourceLineContext(
                    targetSourcePath,
                    sourceLineNumber);

                branchDiagnostics.Add(sourceContext);
            }

            // Cobertura may expose individual conditions beneath
            // <conditions>. Include them where available.
            var conditions = line.Elements()
                .Where(element => element.Name.LocalName == "conditions")
                .SelectMany(element => element.Elements())
                .Where(element => element.Name.LocalName == "condition")
                .ToArray();

            foreach (var condition in conditions)
            {
                var conditionNumber =
                    (string?)condition.Attribute("number") ?? "unknown";

                var conditionType =
                    (string?)condition.Attribute("type") ?? "unknown";

                var conditionPercent =
                    (string?)condition.Attribute("coverage") ?? "unknown";

                branchDiagnostics.Add(
                    $"  Condition {conditionNumber} " +
                    $"(type: {conditionType}): {conditionPercent}.");
            }

            if (conditions.Length == 0)
            {
                branchDiagnostics.Add(
                    "  Individual condition details are not present in this Cobertura report.");
            }
        }

        if (lineCount == 0)
        {
            throw new InvalidDataException(
                $"Cobertura class entry for '{targetRelativePath}' " +
                $"contains no line data in report '{reportPath}'.");
        }

        return new TargetCoverageSnapshot(
            reportPath,
            lineCount,
            linesCovered,
            branchCount,
            branchesCovered,
            branchDiagnostics);
    }

    private static string ReadSourceLineContext(string sourceFilePath, int lineNumber)
    {
        if (!File.Exists(sourceFilePath))
        {
            return "  Source context unavailable: target file not found.";
        }

        var sourceLines = File.ReadAllLines(sourceFilePath);

        if (lineNumber < 1 || lineNumber > sourceLines.Length)
        {
            return $"  Source context unavailable for line {lineNumber}.";
        }

        var sourceLine = sourceLines[lineNumber - 1].Trim();

        return $"  Source code at line {lineNumber}: {sourceLine}";
    }
    private static bool TryReadConditionCounts(
        string? value,
        out int covered,
        out int total)
    {
        covered = 0;
        total = 0;

        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var open = value.IndexOf('(');
        var slash = value.IndexOf('/', open + 1);
        var close = value.IndexOf(')', slash + 1);

        if (open < 0 || slash < 0 || close < 0)
        {
            return false;
        }

        return int.TryParse(
                   value[(open + 1)..slash],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out covered) &&
               int.TryParse(
                   value[(slash + 1)..close],
                   NumberStyles.Integer,
                   CultureInfo.InvariantCulture,
                   out total) &&
               covered >= 0 &&
               total > 0 &&
               covered <= total;
    }

    private sealed record TargetCoverageSnapshot(
        string ReportPath,
        int LineCount,
        int LinesCovered,
        int BranchCount,
        int BranchesCovered,
        IReadOnlyList<string> BranchDiagnostics);
}
