/*
 * Purpose:
 * Defines the request data required to validate generated unit tests.
 *
 * The Function App prepares this request and passes it to the
 * Validation Worker for one validation attempt.
 */

namespace TestEngineer.Contracts;

public sealed class ValidationRequest
{
    public string RequestId { get; set; } = string.Empty;

    public int AttemptNumber { get; set; }

    // Original project ZIP in Blob Storage.
    public string InputBlobName { get; set; } = string.Empty;

    // README-selected production source file, relative to the project.
    public string TargetFilePath { get; set; } = string.Empty;

    // Generated test file location inside the test project.
    public string GeneratedTestFilePath { get; set; } = string.Empty;

    // Blob Storage location containing the generated test file.
    public string GeneratedTestFileBlobName { get; set; } = string.Empty;

    // Project root inside the extracted workspace, when known.
    public string ProjectRootPath { get; set; } = string.Empty;

    // Diagnostics from the previous attempt for Foundry Agent feedback.
    public string PreviousDiagnostics { get; set; } = string.Empty;
}