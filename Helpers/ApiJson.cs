using System.Text.Json;
using System.Text.Json.Serialization;

namespace FabricEmbedSample.Helpers;

/// <summary>JSON options for calling this app's own minimal APIs from Blazor.</summary>
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
}
