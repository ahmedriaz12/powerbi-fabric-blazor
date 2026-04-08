using System.Text.RegularExpressions;

namespace FabricEmbedSample.Models;

/// <summary>
/// Maps to the "PowerBi" section in appsettings / user-secrets.
/// Do not commit ClientSecret — use dotnet user-secrets or environment variables in production.
/// </summary>
public sealed class PowerBiOptions
{
    public const string SectionName = "PowerBi";

    private static readonly Regex GuidRegex = new(
        @"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}",
        RegexOptions.Compiled);

    /// <summary>Azure AD tenant (directory) ID.</summary>
    public string TenantId { get; set; } = "";

    /// <summary>App registration (client) ID.</summary>
    public string ClientId { get; set; } = "";

    /// <summary>App registration client secret Value (password from Certificates and secrets), not the Secret ID column.</summary>
    public string ClientSecret { get; set; } = "";

    /// <summary>Fabric / Power BI workspace ID (group).</summary>
    public string WorkspaceId { get; set; } = "";

    /// <summary>Standard Power BI report backed by a semantic model.</summary>
    public string SemanticReportId { get; set; } = "";

    /// <summary>
    /// Optional dataset GUID for the semantic report. Use when GET report does not return
    /// <c>datasetId</c> but embed still requires Generate Token V2 (e.g. DirectLake).
    /// </summary>
    public string SemanticDatasetId { get; set; } = "";

    /// <summary>Paginated report (RDL) item ID.</summary>
    public string PaginatedReportId { get; set; } = "";

    /// <summary>
    /// Dataset IDs for semantic models used by the paginated report (for Generate Token V2).
    /// If the paginated report uses Power BI datasets, add each ID here — see Microsoft Learn
    /// "Embed a paginated report" token considerations.
    /// </summary>
    public string[]? PaginatedDatasetIds { get; set; }

    /// <summary>Power BI API host (default public cloud).</summary>
    public string ApiHost { get; set; } = "https://api.powerbi.com";

    /// <summary>Azure AD scope for Power BI REST.</summary>
    public string PowerBiScope { get; set; } = "https://analysis.windows.net/powerbi/api/.default";

    /// <summary>
    /// Trims whitespace, removes surrounding &lt; &gt; from secrets/UI copy-paste,
    /// and extracts the first GUID from messy report URL fragments when possible.
    /// </summary>
    public void Normalize()
    {
        TenantId = SanitizeGuidField(TenantId);
        ClientId = SanitizeGuidField(ClientId);
        ClientSecret = SanitizeClientSecret(ClientSecret);
        WorkspaceId = SanitizeGuidField(WorkspaceId);
        SemanticReportId = SanitizeReportId(SemanticReportId);
        SemanticDatasetId = SanitizeGuidField(SemanticDatasetId);
        PaginatedReportId = SanitizeGuidField(PaginatedReportId);
        ApiHost = ApiHost?.Trim() ?? "https://api.powerbi.com";
        PowerBiScope = PowerBiScope?.Trim() ?? "https://analysis.windows.net/powerbi/api/.default";

        if (PaginatedDatasetIds is { Length: > 0 })
            PaginatedDatasetIds = PaginatedDatasetIds
                .Select(SanitizeGuidField)
                .Where(s => !string.IsNullOrEmpty(s))
                .ToArray();
    }

    private static string SanitizeClientSecret(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '<' && s[^1] == '>')
            s = s[1..^1].Trim();
        if (s.Length >= 2 &&
            ((s[0] == '"' && s[^1] == '"') || (s[0] == '\'' && s[^1] == '\'')))
            s = s[1..^1].Trim();
        return s;
    }

    private static string SanitizeGuidField(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '<' && s[^1] == '>')
            s = s[1..^1].Trim();
        var q = s.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            s = s[..q].Trim();
        var match = GuidRegex.Match(s);
        return match.Success ? match.Value : s;
    }

    /// <summary>
    /// Extract report GUID; if a Power BI URL was pasted, use the id after <c>/reports/</c> when present.
    /// </summary>
    private static string SanitizeReportId(string? s)
    {
        if (string.IsNullOrWhiteSpace(s))
            return "";
        s = s.Trim();
        if (s.Length >= 2 && s[0] == '<' && s[^1] == '>')
            s = s[1..^1].Trim();
        var q = s.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
            s = s[..q].Trim();

        const StringComparison ord = StringComparison.OrdinalIgnoreCase;
        var reportsIdx = s.IndexOf("/reports/", ord);
        if (reportsIdx >= 0)
        {
            var afterReports = s[(reportsIdx + "/reports/".Length)..];
            var m = GuidRegex.Match(afterReports);
            if (m.Success)
                return m.Value;
        }

        var fallback = GuidRegex.Match(s);
        return fallback.Success ? fallback.Value : SanitizeGuidField(s);
    }
}
