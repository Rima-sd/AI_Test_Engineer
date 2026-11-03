// Purpose: Gathers trusted platform assemblies and xUnit references for Roslyn compilation.

using Microsoft.CodeAnalysis;

namespace Company.ABC.Vesta.Core.Checks;

public static class CompileReferences
{
    public static IReadOnlyList<MetadataReference> GetReferences()
    {
        var references = new List<MetadataReference>();
        var frameworkDir = Path.GetDirectoryName(typeof(object).Assembly.Location);
        if (!string.IsNullOrEmpty(frameworkDir))
        {
            foreach (var file in Directory.EnumerateFiles(frameworkDir, "*.dll"))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(file));
                }
                catch
                {
                    // Ignore assembly load failures for optional/native DLLs
                }
            }
        }

        var baseDir = AppContext.BaseDirectory;
        if (!string.IsNullOrEmpty(baseDir))
        {
            foreach (var file in Directory.EnumerateFiles(baseDir, "xunit*.dll"))
            {
                try
                {
                    references.Add(MetadataReference.CreateFromFile(file));
                }
                catch
                {
                    // Ignore
                }
            }
        }

        return references;
    }
}