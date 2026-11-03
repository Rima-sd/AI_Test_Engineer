namespace Company.ABC.Transformation;
public sealed class MappingException : System.Exception { }
public static class Mapping
{
    public static Company.ABC.Models.OutputDTO? Map(
        Company.ABC.Models.InputDTO source) => new() { Name = source.Name };
}