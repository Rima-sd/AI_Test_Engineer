// Purpose: Holds the distinct Blob clients for Vesta, Vulcan and Minerva storage accounts.
// Prevents dependency injection from resolving the wrong storage account.

using Azure.Storage.Blobs;

namespace Company.ABC.Vesta.Functions.Storage;

public sealed record BlobServiceClients(
    BlobServiceClient Vesta,
    BlobServiceClient Vulcan,
    BlobServiceClient Minerva);