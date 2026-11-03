// Purpose: Runs manual validation checks for Vesta contract schemas.
// Verifies that valid documents pass and invalid documents produce validation errors.

using System.Globalization;
using System.Text.Json;
using Company.ABC.Vesta.Core.Contracts;
using Company.ABC.Vesta.Core.Jobs;
using Company.ABC.Vesta.Inspect;

// Purpose: Dispatches named Inspect checks while preserving the original
// contract and job validation checks as the default and check-samples command.
if (args.Length > 0 &&
    string.Equals(args[0], "check-input-samples", StringComparison.OrdinalIgnoreCase))
{
    return InputSamplesCheck.Run();
}

if (args.Length > 0 &&
    string.Equals(args[0], "check-agent-samples", StringComparison.OrdinalIgnoreCase))
{
    return await AgentSamplesCheck.RunAsync();
}

if (args.Length > 0 &&
    string.Equals(args[0], "check-pipeline", StringComparison.OrdinalIgnoreCase))
{
    return await PipelineCheck.RunAsync();
}

if (args.Length > 0 &&
    string.Equals(args[0], "check-job-samples", StringComparison.OrdinalIgnoreCase))
{
    return await Company.ABC.Vesta.Inspect.JobCheck.RunAsync();
}

if (args.Length > 0 &&
    string.Equals(args[0], "check-test-samples", StringComparison.OrdinalIgnoreCase))
{
    return await TestCheck.RunAsync();
}

if (args.Length > 0 &&
    !string.Equals(args[0], "check-samples", StringComparison.OrdinalIgnoreCase))
{
    await Console.Error.WriteLineAsync("Available commands: check-samples, check-input-samples, check-agent-samples, check-test-samples, check-pipeline");
    return 2;
}
if (args.Length > 0 &&
    !string.Equals(args[0], "check-samples", StringComparison.OrdinalIgnoreCase))
{
    await Console.Error.WriteLineAsync("Available commands: check-samples, check-input-samples, check-agent-samples, check-pipeline");
    return 2;
}

const string validMessage = """
{
    "messageId": "a1b2c3d4-e5f6-4789-8123-123456789abc",
    "jobId": "price-tiers-code-20261030-01-t01",
    "step": "Generate",
    "round": 1,
    "requestedBy": "system",
    "requestedAt": "2026-11-02T12:00:05Z"
}
""";

const string invalidMessage = """
{
    "messageId": "a1b2c3d4-e5f6-4789-8123-123456789abc",
    "jobId": "price-tiers-code-20261030-01-t01",
    "step": "Generate",
    "round": 1,
    "requestedAt": "2026-11-02T12:00:05Z"
}
""";

Console.WriteLine("Vesta contract validation started.");

using JsonDocument validDocument = JsonDocument.Parse(validMessage);

ContractValidator.Validate(
    ContractKind.StepMessage,
    validDocument.RootElement);

Console.WriteLine("PASS: Valid StepMessage accepted.");

using JsonDocument invalidDocument = JsonDocument.Parse(invalidMessage);

try
{
    ContractValidator.Validate(
        ContractKind.StepMessage,
        invalidDocument.RootElement);

    throw new InvalidOperationException(
        "Expected ContractViolationException was not thrown.");
}
catch (ContractViolationException exception)
{
    Console.WriteLine("PASS: Invalid StepMessage rejected.");

    foreach (string problem in exception.Problems)
    {
        Console.WriteLine($"Validation problem: {problem}");
    }
}

const string validAuditEntry = """
{
    "at": "2026-11-02T12:00:05Z",
    "actor": "system",
    "actorVerified": true,
    "action": "JobCreated",
    "details": {
        "jobId": "price-tiers-code-20261030-01-t01",
        "rowCount": 0
    }
}
""";

const string invalidAuditEntry = """
{
    "at": "2026-11-02T12:00:05Z",
    "actor": "system",
    "actorVerified": true,
    "action": "InvalidAction"
}
""";

using JsonDocument validAuditDocument =
    JsonDocument.Parse(validAuditEntry);

ContractValidator.Validate(
    ContractKind.AuditEntry,
    validAuditDocument.RootElement);

Console.WriteLine("PASS: Valid AuditEntry accepted.");

using JsonDocument invalidAuditDocument =
    JsonDocument.Parse(invalidAuditEntry);

try
{
    ContractValidator.Validate(
        ContractKind.AuditEntry,
        invalidAuditDocument.RootElement);

    throw new InvalidOperationException(
        "Expected ContractViolationException was not thrown.");
}
catch (ContractViolationException exception)
{
    Console.WriteLine("PASS: Invalid AuditEntry rejected.");

    foreach (string problem in exception.Problems)
    {
        Console.WriteLine($"Validation problem: {problem}");
    }
}


// Purpose: Verifies the AuditEntry actor verification rule.
// A system actor may be verified, but a person must not be marked as system-verified.

const string validHumanAuditEntry = """
{
    "at": "2026-11-02T12:00:05Z",
    "actor": "Rima",
    "actorVerified": false,
    "action": "JobCreated",
    "details": {
        "jobId": "price-tiers-code-20261030-01-t01"
    }
}
""";

using JsonDocument validHumanAuditDocument =
    JsonDocument.Parse(validHumanAuditEntry);

ContractValidator.Validate(
    ContractKind.AuditEntry,
    validHumanAuditDocument.RootElement);

Console.WriteLine("PASS: Human actor with actorVerified=false accepted.");

// A human actor must not be marked as system-verified.
const string invalidHumanAuditEntry = """
{
    "at": "2026-11-02T12:00:05Z",
    "actor": "Rima",
    "actorVerified": true,
    "action": "JobCreated",
    "details": {
        "jobId": "price-tiers-code-20261030-01-t01"
    }
}
""";

using JsonDocument invalidHumanAuditDocument =
    JsonDocument.Parse(invalidHumanAuditEntry);

try
{
    ContractValidator.Validate(
        ContractKind.AuditEntry,
        invalidHumanAuditDocument.RootElement);

    throw new InvalidOperationException(
        "Expected human actor with actorVerified=true to be rejected.");
}
catch (ContractViolationException)
{
    Console.WriteLine("PASS: Human actor with actorVerified=true rejected.");
}

// A system actor with actorVerified=true must remain valid.
const string validSystemAuditEntry = """
{
    "at": "2026-11-02T12:00:05Z",
    "actor": "system",
    "actorVerified": true,
    "action": "JobCreated",
    "details": {
        "jobId": "price-tiers-code-20261030-01-t01"
    }
}
""";

using JsonDocument validSystemAuditDocument =
    JsonDocument.Parse(validSystemAuditEntry);

ContractValidator.Validate(
    ContractKind.AuditEntry,
    validSystemAuditDocument.RootElement);

Console.WriteLine("PASS: System actor with actorVerified=true accepted.");

const string validJob = """
{
    "jobId": "price-tiers-code-20261030-01-t01",
    "vulcanJobId": "price-tiers-code-20261030-01",
    "mappingId": "mapping-001",
    "createdBy": "system",
    "createdAt": "2026-11-02T12:00:00Z",
    "origin": {
        "vulcanGeneration": 1
    },
    "project": {
        "fullName": "Company.ABC.PriceTiers"
    },
    "state": "Received",
    "round": 0,
    "repairRoundsUsed": 0,
    "inputs": null,
    "check": null,
    "usage": {
        "agentCalls": 0,
        "inputTokens": 0,
        "outputTokens": 0
    },
    "output": null,
    "lastError": null,
    "updatedAt": "2026-11-02T12:00:00Z"
}
""";

const string invalidJob = """
{
    "jobId": "INVALID-JOB-ID",
    "vulcanJobId": "INVALID-VULCAN-ID",
    "mappingId": "mapping-001",
    "createdBy": "system",
    "createdAt": "2026-11-02T12:00:00Z",
    "origin": {
        "vulcanGeneration": 0
    },
    "project": {
        "fullName": "Company.ABC.PriceTiers"
    },
    "state": "Unknown",
    "round": -1,
    "repairRoundsUsed": 0,
    "inputs": null,
    "check": null,
    "usage": {
        "agentCalls": 0,
        "inputTokens": 0,
        "outputTokens": 0
    },
    "output": null,
    "lastError": null,
    "updatedAt": "2026-11-02T12:00:00Z"
}
""";

using JsonDocument validJobDocument = JsonDocument.Parse(validJob);

ContractValidator.Validate(
    ContractKind.Job,
    validJobDocument.RootElement);

Console.WriteLine("PASS: Valid JobDocument accepted.");

using JsonDocument invalidJobDocument = JsonDocument.Parse(invalidJob);

try
{
    ContractValidator.Validate(
        ContractKind.Job,
        invalidJobDocument.RootElement);

    throw new InvalidOperationException(
        "Expected ContractViolationException was not thrown.");
}
catch (ContractViolationException exception)
{
    Console.WriteLine("PASS: Invalid JobDocument rejected.");

    foreach (string problem in exception.Problems)
    {
        Console.WriteLine($"Validation problem: {problem}");
    }
}

// Purpose: Verifies that optional AuditEntry state properties are omitted when null.

AuditEntry auditEntry = new(
    At: DateTimeOffset.Parse("2026-11-02T12:00:05Z", CultureInfo.InvariantCulture),
    Actor: "system",
    ActorVerified: true,
    Action: AuditAction.JobCreated,
    FromState: null,
    ToState: null,
    Details: new Dictionary<string, object?>
    {
        ["jobId"] = "price-tiers-code-20261030-01-t01"
    });

string auditJson = JsonSerializer.Serialize(
    auditEntry,
    ContractJson.Options);

Console.WriteLine("AuditEntry serialization:");
Console.WriteLine(auditJson);

using JsonDocument auditJsonDocument = JsonDocument.Parse(auditJson);

if (auditJsonDocument.RootElement.TryGetProperty("fromState", out _)
    || auditJsonDocument.RootElement.TryGetProperty("toState", out _))
{
    throw new InvalidOperationException(
        "Null state properties should not appear in serialized AuditEntry.");
}

Console.WriteLine("PASS: Null AuditEntry state properties omitted.");

// Purpose: Verifies Vesta StepMessage JSON serialization conventions.

StepMessage stepMessage = new(
    MessageId: Guid.Parse("a1b2c3d4-e5f6-4789-8123-123456789abc"),
    JobId: "price-tiers-code-20261030-01-t01",
    Step: VestaStep.Generate,
    Round: 1,
    RequestedBy: "system",
    RequestedAt: DateTimeOffset.Parse(
        "2026-11-02T12:00:05Z",
        CultureInfo.InvariantCulture));

string stepMessageJson = JsonSerializer.Serialize(
    stepMessage,
    ContractJson.Options);

Console.WriteLine("StepMessage serialization:");
Console.WriteLine(stepMessageJson);

using JsonDocument stepMessageDocument =
    JsonDocument.Parse(stepMessageJson);

JsonElement stepRoot = stepMessageDocument.RootElement;

if (!stepRoot.TryGetProperty("step", out JsonElement stepElement)
    || stepElement.GetString() != "Generate")
{
    throw new InvalidOperationException(
        "Step enum was not serialized as a string.");
}

if (!stepRoot.TryGetProperty("jobId", out _))
{
    throw new InvalidOperationException(
        "Property names were not serialized using camelCase.");
}

Console.WriteLine("PASS: StepMessage uses camelCase and string enums.");

// Purpose: Verifies Vulcan and Vesta job ID validation and generation.

Console.WriteLine();
Console.WriteLine("Job ID validation checks:");

const string validVulcanId = "price-tiers-code-20261030-01";
const string validVestaId = "price-tiers-code-20261030-01-t01";

if (!JobIds.IsValidVulcanJobId(validVulcanId))
{
    throw new InvalidOperationException("Valid Vulcan job ID was rejected.");
}

Console.WriteLine("PASS: Valid Vulcan job ID accepted.");

if (!JobIds.IsValidVestaJobId(validVestaId))
{
    throw new InvalidOperationException("Valid Vesta job ID was rejected.");
}

Console.WriteLine("PASS: Valid Vesta job ID accepted.");

if (JobIds.IsValidVulcanJobId("invalid-vulcan-id"))
{
    throw new InvalidOperationException("Invalid Vulcan job ID was accepted.");
}

Console.WriteLine("PASS: Invalid Vulcan job ID rejected.");

if (JobIds.IsValidVestaJobId("invalid-vesta-id"))
{
    throw new InvalidOperationException("Invalid Vesta job ID was accepted.");
}

Console.WriteLine("PASS: Invalid Vesta job ID rejected.");

string generatedId = JobIds.CreateVestaJobId(validVulcanId, 2);

if (generatedId != "price-tiers-code-20261030-01-t02")
{
    throw new InvalidOperationException("Vesta job ID generation failed.");
}

Console.WriteLine("PASS: Vesta job ID generated correctly.");

// Purpose: Verifies Vesta storage path construction and path safety.

Console.WriteLine();
Console.WriteLine("Job path validation checks:");

const string testVestaJobId = "price-tiers-code-20261030-01-t01";
const string testVulcanJobId = "price-tiers-code-20261030-01";

if (JobPaths.JobDocument(testVestaJobId)
    != $"{testVestaJobId}/job.json")
{
    throw new InvalidOperationException("Job document path generation failed.");
}

Console.WriteLine("PASS: Job document path generated.");

if (JobPaths.AuditLog(testVestaJobId)
    != $"{testVestaJobId}/audit.log")
{
    throw new InvalidOperationException("Audit log path generation failed.");
}

Console.WriteLine("PASS: Audit log path generated.");

if (JobPaths.JobFile(testVestaJobId, "tests/MappingTests.cs")
    != $"{testVestaJobId}/tests/MappingTests.cs")
{
    throw new InvalidOperationException("Job file path generation failed.");
}

Console.WriteLine("PASS: Job file path generated.");

if (JobPaths.VulcanJobDocument(testVulcanJobId)
    != $"{testVulcanJobId}/job.json")
{
    throw new InvalidOperationException("Vulcan job document path generation failed.");
}

Console.WriteLine("PASS: Vulcan job document path generated.");

if (JobPaths.VulcanPackage(testVulcanJobId, "packages/source.zip")
    != $"{testVulcanJobId}/packages/source.zip")
{
    throw new InvalidOperationException("Vulcan package path generation failed.");
}

Console.WriteLine("PASS: Vulcan package path generated.");

if (JobPaths.MinervaTestCases("mapping-001")
    != "mapping-001/test-cases.json")
{
    throw new InvalidOperationException("Minerva test cases path generation failed.");
}

Console.WriteLine("PASS: Minerva test cases path generated.");

if (JobPaths.OutputZip(testVestaJobId, 2)
    != $"{testVestaJobId}/output/round-2.zip")
{
    throw new InvalidOperationException("Output ZIP path generation failed.");
}

Console.WriteLine("PASS: Output ZIP path generated.");

// Unsafe path must be rejected.
try
{
    _ = JobPaths.JobFile(testVestaJobId, "../outside.txt");
    throw new InvalidOperationException("Unsafe relative path was accepted.");
}
catch (ArgumentException)
{
    Console.WriteLine("PASS: Unsafe relative path rejected.");
}

// Reserved job document path must be rejected.
try
{
    _ = JobPaths.JobFile(testVestaJobId, "job.json");
    throw new InvalidOperationException("Reserved job.json path was accepted.");
}
catch (ArgumentException)
{
    Console.WriteLine("PASS: Reserved job.json path rejected.");
}

Console.WriteLine("PASS: Job path validation checks completed.");

Console.WriteLine();
Console.WriteLine("Vesta contract and job ID validation completed.");
return 0;