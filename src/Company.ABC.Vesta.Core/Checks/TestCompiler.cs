// Purpose: Compiles test files and AI-written code in memory using Roslyn.

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Company.ABC.Vesta.Core.Checks;

public static class TestCompiler
{
    public static CompilationResult Compile(string testFileContent, IEnumerable<string> sourceFilesContent)
    {
        var syntaxTrees = new List<SyntaxTree>
        {
            CSharpSyntaxTree.ParseText(testFileContent, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Default))
        };

        foreach (var source in sourceFilesContent)
        {
            syntaxTrees.Add(CSharpSyntaxTree.ParseText(source, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Default)));
        }

        const string globalUsings = @"
global using global::System;
global using global::System.Collections.Generic;
global using global::System.IO;
global using global::System.Linq;
global using global::System.Net.Http;
global using global::System.Threading;
global using global::System.Threading.Tasks;
";
        syntaxTrees.Add(CSharpSyntaxTree.ParseText(globalUsings));

        var compilation = CSharpCompilation.Create(
            "VestaTestAssembly",
            syntaxTrees,
            CompileReferences.GetReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
                .WithNullableContextOptions(NullableContextOptions.Enable)
                .WithOptimizationLevel(OptimizationLevel.Debug));

        using var ms = new MemoryStream();
        var emitResult = compilation.Emit(ms);

        return new CompilationResult(emitResult.Success, compilation.GetDiagnostics());
    }
}

public sealed record CompilationResult(bool Success, IEnumerable<Diagnostic> Diagnostics);