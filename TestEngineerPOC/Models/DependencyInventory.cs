
namespace TestEngineerPOC.Models;

public sealed class DependencyInventory
{
    public string FilePath { get; set; } = string.Empty;

    public List<string> TypeNames { get; set; } = new();

    public List<string> MethodNames { get; set; } = new();

    public List<string> PropertyNames { get; set; } = new();

    public List<string> ConstructorNames { get; set; } = new();

    public List<string> BaseTypes { get; set; } = new();
}
