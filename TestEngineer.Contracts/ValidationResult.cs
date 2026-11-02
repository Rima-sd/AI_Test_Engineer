
/*
 * Purpose:
 * Represents the validation outcome of a single test-generation attempt.
 *
 * CoveragePercent is retained for backward compatibility and mirrors
 * LineCoveragePercent. Acceptance uses line and branch coverage separately.
 */

namespace TestEngineer.Contracts;

public sealed class ValidationResult
{
    public string RequestId { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    public bool BuildPassed { get; set; }

    public bool TestsPassed { get; set; }

    public double LineCoveragePercent { get; set; }

    public double BranchCoveragePercent { get; set; }

    // Backward-compatible property for existing Function App code.
    public double CoveragePercent
    {
        get => LineCoveragePercent;
        set => LineCoveragePercent = value;
    }

    public int SurvivingMutants { get; set; }

    public bool QualityChecksPassed { get; set; }

    public string Diagnostics { get; set; } = string.Empty;

    public bool Passed =>
        BuildPassed &&
        TestsPassed &&
        LineCoveragePercent >= 100.0 &&
        BranchCoveragePercent >= 100.0 &&
        SurvivingMutants == 0 &&
        QualityChecksPassed;
}
