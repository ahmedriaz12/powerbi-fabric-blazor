using System.Text.Json.Serialization;

namespace FabricEmbedSample.Models;

public enum EmbedReportKind
{
    Semantic,
    Paginated
}

/// <summary>Returned to the Blazor client for embedding.</summary>
public sealed class EmbedConfigDto
{
    public string EmbedToken { get; set; } = "";
    public string EmbedUrl { get; set; } = "";
    public string ReportId { get; set; } = "";
    public DateTimeOffset? Expiration { get; set; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public EmbedReportKind Kind { get; set; }

    /// <summary>Server-side phase timings (ms) for troubleshooting slow loads.</summary>
    public Dictionary<string, long> TimingsMs { get; set; } = new();

    /// <summary>How the embed token was produced (for support).</summary>
    public string TokenMode { get; set; } = "";
}

internal sealed class PowerBiReportApiResponse
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("embedUrl")]
    public string? EmbedUrl { get; set; }

    /// <summary>Bound dataset (required for Generate Token V2 / DirectLake).</summary>
    [JsonPropertyName("datasetId")]
    public string? DatasetId { get; set; }
}

internal sealed class GenerateTokenV1Response
{
    [JsonPropertyName("token")]
    public string? Token { get; set; }

    [JsonPropertyName("expiration")]
    public DateTimeOffset? Expiration { get; set; }
}
