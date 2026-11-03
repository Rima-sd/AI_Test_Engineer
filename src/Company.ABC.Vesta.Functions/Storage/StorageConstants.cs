// Purpose: Centralizes storage content types and expected HTTP status codes.
namespace Company.ABC.Vesta.Functions.Storage;

public static class StorageConstants
{
    public const string JsonContentType = "application/json";
    public const string ZipContentType = "application/zip";
    public const int NotFound = 404;
    public const int Conflict = 409;
    public const int PreconditionFailed = 412;
}
