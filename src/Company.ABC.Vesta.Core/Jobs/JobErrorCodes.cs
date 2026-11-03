
// Purpose: Centralises the error codes used throughout Vesta job processing.
// Keeps error identifiers consistent across Core, Functions and Foundry.

namespace Company.ABC.Vesta.Core.Jobs;

/// <summary>
/// Defines the standard error codes used by Vesta.
/// </summary>
public static class JobErrorCodes
{
    // Vulcan source validation errors.
    public const string VulcanJobNotFound = "VulcanJobNotFound";
    public const string VulcanJobNotDone = "VulcanJobNotDone";

    // Source code and package validation errors.
    public const string CodeDoesNotCompile = "CodeDoesNotCompile";
    public const string ZipNotFound = "ZipNotFound";
    public const string ZipUnsafe = "ZipUnsafe";
    public const string LayoutWrong = "LayoutWrong";
    public const string TestProjectUnsupported = "TestProjectUnsupported";
    public const string PackageInvalid = "PackageInvalid";

    // Minerva test-case validation errors.
    public const string TestCasesInvalid = "TestCasesInvalid";
    public const string InputTooLarge = "InputTooLarge";

    // Foundry agent and generated-test errors.
    public const string AgentRefused = "AgentRefused";
    public const string AgentReplyUnusable = "AgentReplyUnusable";
    public const string UnsafeTests = "UnsafeTests";

    // Contract validation error.
    public const string ContractViolation = "ContractViolation";
}
