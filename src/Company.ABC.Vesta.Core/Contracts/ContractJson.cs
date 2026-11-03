// Purpose: Provides centralized JSON serialization settings for Vesta contracts.
// Ensures consistent property naming, enum handling and strict deserialization.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Company.ABC.Vesta.Core.Contracts;

/// <summary>
/// Provides the shared JSON serialization options for Vesta contracts.
/// </summary>
public static class ContractJson
{
    /// <summary>
    /// Gets the JSON serialization options used for Vesta-owned documents.
    /// </summary>
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DictionaryKeyPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            WriteIndented = false
        };

        options.Converters.Add(new JsonStringEnumConverter());

        return options;
    }
}