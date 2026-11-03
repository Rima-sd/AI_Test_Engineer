// Purpose: Constants for test check problem codes.

namespace Company.ABC.Vesta.Core.Checks;

public static class ProblemCodes
{
    public const string TestFileTooLarge = "TestFileTooLarge";
    public const string ForbiddenApi = "ForbiddenApi";
    public const string ForbiddenUsing = "ForbiddenUsing";
    public const string ParseError = "ParseError";
    public const string WrongNamespace = "WrongNamespace";
    public const string WrongClass = "WrongClass";
    public const string NonDeterministic = "NonDeterministic";
    public const string SuppressedWarning = "SuppressedWarning";
    public const string SkippedTest = "SkippedTest";
    public const string TestWithoutAssert = "TestWithoutAssert";
    public const string TrivialAssert = "TrivialAssert";
    public const string MapNotCalled = "MapNotCalled";
    public const string TestWithoutRow = "TestWithoutRow";
    public const string UnknownRow = "UnknownRow";
    public const string RowNotCovered = "RowNotCovered";
    public const string NotTestableNotAllowed = "NotTestableNotAllowed";
    public const string ExampleNotUsed = "ExampleNotUsed";
    public const string CompileError = "CompileError";
    public const string AnalyzerError = "AnalyzerError";
}