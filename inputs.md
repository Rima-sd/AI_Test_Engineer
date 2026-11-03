# Input handling and validation

This document describes the input readers and checks currently implemented in `Company.ABC.Vesta.Core.Inputs`. It documents the code as supplied for the Vesta MVP; it does not define additional storage paths or orchestration behavior.

## Input sequence

The input layer has four main responsibilities:

1. Read and validate the Vulcan job document.
2. Read the Minerva package and its mapping contract.
3. Read optional Minerva test cases and relate them to mapping rows.
4. Read the Vulcan project ZIP in memory, validate its layout and safety, and calculate the content size that will be sent to the agent.

The readers return result objects containing success/validity information, an error code where applicable, and a list of problems. Invalid input is reported as a result rather than being silently repaired.

## 1. Vulcan job document

`VulcanJobReader.Read` accepts the job document as `ReadOnlyMemory<byte>?`.

The document must be a JSON object with the fields used by Vesta:

- `jobId`
- `mappingId`
- `state`
- `origin.kind`
- `origin.runId`
- `origin.packageVersion`
- `project.fullName`
- `generation`
- `output.zipPath`
- `output.compiled`
- `output.generation`

The reader validates the job ID using `JobIds.IsValidVulcanJobId`, requires a non-empty mapping ID and project full name, and requires both generation values to be greater than zero. For a `Done` job, `output.zipPath` must be non-empty.

Supported origin kinds are:

- `Minerva`: `origin.runId` must be non-empty and `origin.packageVersion` must be greater than zero.
- `Upload`: `origin.runId` and `origin.packageVersion` must both be null.

The job is accepted only when `state` is exactly `Done` and `output.compiled` is `true`. Missing or empty content is reported as `VulcanJobNotFound`; invalid JSON or field/contract problems are reported as `ContractViolation`; a non-Done job is `VulcanJobNotDone`; and an output with `compiled` set to false is `CodeDoesNotCompile`.

## 2. Minerva package JSON

`PackageReader.Read` accepts the package document as `ReadOnlyMemory<byte>?`. The root must be an object containing a `Files` array.

The current reader expects exactly three entries. Each entry has these fields:

- `FileName`
- `ContentType`
- `Content`
- `Remarks`

The reader identifies the entries by `Remarks`, expecting one each for `source`, `target` and `mapping`. It validates that the three roles are present uniquely, then parses and validates the mapping content.

The accepted mapping `contractVersion` is `0.1`. The mapping must contain the mapping identifier, scopes and rows. Each mapping row is converted into the fields used by Vesta:

- `rowId`
- `scopeId`
- `target.path`
- `rule.type`

The parsed row list is exposed through `RowListBuilder.Build`, which sorts rows by `RowId` using ordinal ordering. This gives later processing a deterministic row order.

## 3. Minerva test-cases JSON

`TestCasesReader.Read` accepts optional JSON content and the parsed mapping.

- If the content is absent (`null`), the reader returns `Found = false`, no cases and no error.
- If content is supplied, the root must contain a `cases` array.
- Each case must contain non-empty `rowId` and `targetPath` strings, plus `input` and `expected` JSON values.
- The optional `synthetic` field must be a JSON boolean; when omitted it defaults to `false`.
- Every case `rowId` must match a row in the parsed mapping.

Invalid JSON or invalid case structure/references produces `TestCasesInvalid` and one or more problem descriptions. JSON values for `input` and `expected` are cloned from the parsed document so they remain usable after the document is disposed.

## 4. Project ZIP

`ProjectZipReader.Read` accepts the ZIP bytes as `ReadOnlyMemory<byte>?` and an `InputLimits` instance. It reads archive entries into memory and returns a `ProjectZip` containing a path-to-content dictionary, the total uncompressed byte count and the detected top-folder name.

### Archive checks

The reader checks:

- ZIP content exists and is not empty.
- Compressed archive size does not exceed `MaxZipBytes`.
- Entry count does not exceed `MaxZipEntries`.
- Entry paths are not empty, absolute, backslash-based or drive-qualified.
- Path segments do not contain empty segments, `.` or `..`.
- Symbolic-link entries are rejected.
- Duplicate entry names are rejected case-insensitively.
- Total uncompressed bytes do not exceed `MaxZipUnpackedBytes`.
- A root-level `COMPILE-ERRORS.md` is rejected.

A ZIP may have one common top-level directory. When one is detected, that prefix is removed from stored entry paths, so the resulting `ProjectZip.Files` paths are relative to the project root. A ZIP without a common top-level directory is also supported.

The archive is processed through memory streams; the reader does not extract the project to disk. Invalid/corrupted archive data and read failures are returned as ZIP safety failures.

### Required project layout

`LayoutChecker.Check` checks for these paths relative to the project root:

```text
Models/InputDTO.cs
Models/OutputDTO.cs
Transformation/Mapping.cs
Transformation/MappingLogic.md
Tests/Tests.csproj
Tests/Transformation/MappingTests.cs
```

It also checks that:

- Exactly one `.csproj` exists at the project root.
- The placeholder `Tests/Transformation/MappingTests.cs` contains a namespace declaration.
- `Tests/Tests.csproj` is valid XML.
- The test project targets the same framework as the root project.
- The test project references `Microsoft.NET.Test.Sdk`.
- The test project uses xUnit major version 2.

Layout problems and unsupported test-project conditions are returned in the `LayoutCheckResult` error-code and problem collections.

## 5. Agent input size

`InputSizeChecker.Check` calculates the total input byte count as:

- the complete Minerva package JSON byte length; plus
- the bytes of each of the following files when present in the project ZIP:
  - `Models/InputDTO.cs`
  - `Models/OutputDTO.cs`
  - `Transformation/Mapping.cs`
  - `Transformation/MappingLogic.md`

The total must not exceed `MaxAgentInputBytes`. If it does, the result uses `InputTooLarge` and includes the measured and maximum byte counts.

## 6. Configured limits

`InputLimits` is an immutable record. Its constructor rejects zero or negative values for every limit.

| Setting | Default documented in root README | Meaning |
|---|---:|---|
| `Limits__MaxZipBytes` | `52428800` | Maximum compressed ZIP bytes |
| `Limits__MaxZipEntries` | `5000` | Maximum ZIP entry count |
| `Limits__MaxZipUnpackedBytes` | `209715200` | Maximum total uncompressed ZIP bytes |
| `Limits__MaxAgentInputBytes` | `1048576` | Maximum calculated agent-input bytes |
| `Limits__MaxTestFileBytes` | `262144` | Maximum test-file bytes |

`MaxTestFileBytes` is part of the limits object. The input readers described above do not themselves apply this limit; it is available to the later test-file handling stage.

## 7. Sample checks

Run both Inspect commands from the repository root:

```powershell
dotnet run --project .\tools\Company.ABC.Vesta.Inspect -- check-samples
dotnet run --project .\tools\Company.ABC.Vesta.Inspect -- check-input-samples
```

The input sample check covers valid and invalid job/package/test-case documents, ZIP layouts, unsafe ZIP entries and configured size limits. ZIP fixtures are generated at runtime from `samples/inputs/zips-src/`, so generated binary ZIP files are not required in source control.

A passing sample check verifies the documented sample scenarios. It does not replace a repository build or prove that the full end-to-end workflow is complete.
