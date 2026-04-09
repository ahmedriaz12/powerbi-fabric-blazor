using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FabricEmbedSample.Models;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace FabricEmbedSample.Services;

public sealed class PowerBiEmbedService
{
    /// <summary>Name registered with <see cref="IHttpClientFactory"/> for Power BI REST calls.</summary>
    public const string PowerBiHttpClientName = "PowerBi";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly PowerBiOptions _options;
    private readonly IConfidentialClientApplication _msalApp;
    private readonly IHttpClientFactory _httpFactory;
    private readonly ILogger<PowerBiEmbedService> _logger;

    public PowerBiEmbedService(
        IOptions<PowerBiOptions> options,
        IConfidentialClientApplication msalApp,
        IHttpClientFactory httpFactory,
        ILogger<PowerBiEmbedService> logger)
    {
        _options = options.Value;
        _msalApp = msalApp;
        _httpFactory = httpFactory;
        _logger = logger;
    }

    public async Task<EmbedConfigDto> GetEmbedConfigAsync(
        EmbedReportKind kind,
        EffectiveIdentityInput? effectiveIdentity = null,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();

        var timings = new Dictionary<string, long>();
        var swTotal = Stopwatch.StartNew();

        var aadSw = Stopwatch.StartNew();
        var powerBiAccessToken = await AcquirePowerBiAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        timings["aad_token_ms"] = aadSw.ElapsedMilliseconds;

        var workspaceId = _options.WorkspaceId;
        var reportId = kind == EmbedReportKind.Semantic
            ? _options.SemanticReportId
            : _options.PaginatedReportId;

        var http = _httpFactory.CreateClient(PowerBiHttpClientName);

        var metaSw = Stopwatch.StartNew();
        var report = await GetReportInGroupAsync(http, powerBiAccessToken, workspaceId, reportId, cancellationToken)
            .ConfigureAwait(false);
        timings["get_report_ms"] = metaSw.ElapsedMilliseconds;

        if (string.IsNullOrWhiteSpace(report.EmbedUrl))
            throw new InvalidOperationException("Power BI returned no embedUrl for this report. Check workspace and report IDs.");

        EffectiveIdentityEchoDto? identityEcho = null;
        if (effectiveIdentity is not null && !string.IsNullOrWhiteSpace(effectiveIdentity.Username))
            identityEcho = new EffectiveIdentityEchoDto
            {
                Username = effectiveIdentity.Username.Trim(),
                Roles = effectiveIdentity.Roles.ToList()
            };

        string token;
        DateTimeOffset? expiration;
        string tokenMode;

        var tokenSw = Stopwatch.StartNew();
        if (kind == EmbedReportKind.Semantic)
        {
            // DirectLake / Fabric: "Embedding a DirectLake dataset is not supported with V1 embed token" → use V2 with dataset.
            var semanticDataset = !string.IsNullOrWhiteSpace(_options.SemanticDatasetId)
                ? _options.SemanticDatasetId
                : report.DatasetId;

            if (!string.IsNullOrWhiteSpace(semanticDataset))
            {
                if (identityEcho is not null)
                    identityEcho.DatasetIds = new[] { semanticDataset };

                (token, expiration) = await GenerateTokenV2Async(
                        http,
                        powerBiAccessToken,
                        reportId,
                        [semanticDataset],
                        effectiveIdentity,
                        cancellationToken)
                    .ConfigureAwait(false);
                tokenMode = effectiveIdentity is { Username: var u } && !string.IsNullOrWhiteSpace(u)
                    ? "GenerateToken V2 (semantic + dataset + EffectiveIdentity)"
                    : "GenerateToken V2 (semantic + dataset; DirectLake / Fabric)";
            }
            else
            {
                var dsForV1Identity = report.DatasetId;
                if (identityEcho is not null)
                {
                    if (string.IsNullOrWhiteSpace(dsForV1Identity))
                        throw new InvalidOperationException(
                            "Effective identity requires a dataset id on the report. Set PowerBi:SemanticDatasetId or use a report that returns datasetId.");
                    identityEcho.DatasetIds = new[] { dsForV1Identity };
                }

                (token, expiration) = await GenerateTokenReportV1Async(
                        http,
                        powerBiAccessToken,
                        workspaceId,
                        reportId,
                        effectiveIdentity,
                        dsForV1Identity,
                        cancellationToken)
                    .ConfigureAwait(false);
                tokenMode = effectiveIdentity is { Username: var u2 } && !string.IsNullOrWhiteSpace(u2)
                    ? "GenerateToken (report in group + EffectiveIdentity)"
                    : "GenerateToken (report in group)";
            }
        }
        else
        {
            (token, expiration, tokenMode) = await GenerateTokenForPaginatedAsync(
                http, powerBiAccessToken, workspaceId, reportId, report, effectiveIdentity, cancellationToken).ConfigureAwait(false);
            if (identityEcho is not null && _options.PaginatedDatasetIds is { Length: > 0 })
                identityEcho.DatasetIds = _options.PaginatedDatasetIds;
            else if (identityEcho is not null && !string.IsNullOrWhiteSpace(report.DatasetId))
                identityEcho.DatasetIds = new[] { report.DatasetId };
        }

        timings["generate_embed_token_ms"] = tokenSw.ElapsedMilliseconds;
        timings["total_server_ms"] = swTotal.ElapsedMilliseconds;

        _logger.LogInformation(
            "Embed config for {Kind}: tokenMode={TokenMode}, timings={Timings}, effectiveIdentity={HasId}",
            kind, tokenMode, timings, identityEcho is not null);

        return new EmbedConfigDto
        {
            EmbedToken = token,
            EmbedUrl = report.EmbedUrl!,
            ReportId = reportId,
            Expiration = expiration,
            Kind = kind,
            TimingsMs = timings,
            TokenMode = tokenMode,
            EffectiveIdentity = identityEcho
        };
    }

    private async Task<(string Token, DateTimeOffset? Expiration, string Mode)> GenerateTokenForPaginatedAsync(
        HttpClient http,
        string powerBiAccessToken,
        string workspaceId,
        string reportId,
        PowerBiReportApiResponse reportMeta,
        EffectiveIdentityInput? effectiveIdentity,
        CancellationToken ct)
    {
        var datasetIds = _options.PaginatedDatasetIds;
        if (datasetIds is { Length: > 0 })
        {
            var (t, e) = await GenerateTokenV2Async(http, powerBiAccessToken, reportId, datasetIds, effectiveIdentity, ct)
                .ConfigureAwait(false);
            var mode = effectiveIdentity is { Username: var u } && !string.IsNullOrWhiteSpace(u)
                ? "GenerateToken V2 (paginated + datasets + EffectiveIdentity)"
                : "GenerateToken V2 (datasets + report, no targetWorkspaces)";
            return (t, e, mode);
        }

        try
        {
            var (t, e) = await GenerateTokenReportV1Async(
                    http,
                    powerBiAccessToken,
                    workspaceId,
                    reportId,
                    effectiveIdentity,
                    reportMeta.DatasetId,
                    ct)
                .ConfigureAwait(false);
            var mode = effectiveIdentity is { Username: var u } && !string.IsNullOrWhiteSpace(u)
                ? "GenerateToken (paginated V1 + EffectiveIdentity)"
                : "GenerateToken (report in group) — paginated without dataset list";
            return (t, e, mode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "V1 paginated token failed; configure PowerBi:PaginatedDatasetIds if the report uses semantic models.");
            throw new InvalidOperationException(
                "Paginated report token failed. If this report uses Power BI semantic models as data sources, " +
                "add their dataset GUIDs to PowerBi:PaginatedDatasetIds in configuration (see Microsoft Learn: Embed a paginated report — Token considerations).",
                ex);
        }
    }

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.TenantId))
            throw new InvalidOperationException("PowerBi:TenantId is required.");
        if (string.IsNullOrWhiteSpace(_options.ClientId))
            throw new InvalidOperationException("PowerBi:ClientId is required.");
        if (string.IsNullOrWhiteSpace(_options.ClientSecret))
            throw new InvalidOperationException("PowerBi:ClientSecret is required (use user-secrets for local dev).");
        if (string.IsNullOrWhiteSpace(_options.WorkspaceId))
            throw new InvalidOperationException("PowerBi:WorkspaceId is required.");
        if (string.IsNullOrWhiteSpace(_options.SemanticReportId))
            throw new InvalidOperationException("PowerBi:SemanticReportId is required.");
        if (string.IsNullOrWhiteSpace(_options.PaginatedReportId))
            throw new InvalidOperationException("PowerBi:PaginatedReportId is required.");
    }

    private async Task<string> AcquirePowerBiAccessTokenAsync(CancellationToken cancellationToken)
    {
        var scopes = new[] { _options.PowerBiScope };
        var result = await _msalApp.AcquireTokenForClient(scopes)
            .ExecuteAsync(cancellationToken)
            .ConfigureAwait(false);

        return result.AccessToken;
    }

    private static async Task<PowerBiReportApiResponse> GetReportInGroupAsync(
        HttpClient http,
        string bearerToken,
        string groupId,
        string reportId,
        CancellationToken ct)
    {
        var url = $"groups/{Uri.EscapeDataString(groupId)}/reports/{Uri.EscapeDataString(reportId)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var report = JsonSerializer.Deserialize<PowerBiReportApiResponse>(json, JsonOptions);
        if (report is null)
            throw new InvalidOperationException("Failed to deserialize report metadata.");
        return report;
    }

    private async Task<(string Token, DateTimeOffset? Expiration)> GenerateTokenReportV1Async(
        HttpClient http,
        string bearerToken,
        string groupId,
        string reportId,
        EffectiveIdentityInput? effectiveIdentity,
        string? reportDatasetId,
        CancellationToken ct)
    {
        var url = $"groups/{Uri.EscapeDataString(groupId)}/reports/{Uri.EscapeDataString(reportId)}/GenerateToken";
        object bodyObj = BuildV1TokenBody(effectiveIdentity, reportDatasetId);
        var body = JsonSerializer.Serialize(bodyObj, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var parsed = JsonSerializer.Deserialize<GenerateTokenV1Response>(json, JsonOptions);
        if (string.IsNullOrEmpty(parsed?.Token))
            throw new InvalidOperationException("GenerateToken returned no token.");
        return (parsed.Token, parsed.Expiration);
    }

    /// <summary>
    /// Read-only embed: datasets + report only (see Microsoft Learn paginated embed token sample — no targetWorkspaces for view).
    /// </summary>
    private async Task<(string Token, DateTimeOffset? Expiration)> GenerateTokenV2Async(
        HttpClient http,
        string bearerToken,
        string reportId,
        string[] datasetIds,
        EffectiveIdentityInput? effectiveIdentity,
        CancellationToken ct)
    {
        var trimmedDatasets = datasetIds
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .ToArray();

        var requestBody = new GenerateTokenV2Request
        {
            Reports =
            [
                new GenerateTokenV2ReportRef { Id = reportId, AllowEdit = false }
            ],
            Datasets = trimmedDatasets
                .Select(id => new GenerateTokenV2DatasetRef
                {
                    Id = id,
                    XmlaPermissions = "ReadOnly"
                })
                .ToList(),
            Identities = BuildIdentitiesForApi(effectiveIdentity, trimmedDatasets)
        };

        var url = "GenerateToken";
        var body = JsonSerializer.Serialize(requestBody, JsonOptions);
        using var content = new StringContent(body, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = content };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearerToken);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        await EnsureSuccessOrThrowAsync(response, ct).ConfigureAwait(false);
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var token = root.GetProperty("token").GetString();
        DateTimeOffset? expiration = null;
        if (root.TryGetProperty("expiration", out var expEl))
        {
            var s = expEl.GetString();
            if (DateTimeOffset.TryParse(s, out var dto))
                expiration = dto;
        }

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException("GenerateToken V2 returned no token.");
        return (token, expiration);
    }

    private static List<GenerateTokenV2Identity>? BuildIdentitiesForApi(
        EffectiveIdentityInput? identity,
        string[] datasetGuids)
    {
        if (identity is null || string.IsNullOrWhiteSpace(identity.Username))
            return null;
        if (datasetGuids.Length == 0)
            throw new InvalidOperationException(
                "Effective identity requires at least one dataset GUID (check SemanticDatasetId / PaginatedDatasetIds).");

        return new List<GenerateTokenV2Identity>
        {
            new()
            {
                Username = identity.Username.Trim(),
                Roles = identity.Roles.Count > 0 ? identity.Roles.ToList() : new List<string>(),
                Datasets = datasetGuids.ToList()
            }
        };
    }

    private static object BuildV1TokenBody(EffectiveIdentityInput? identity, string? reportDatasetId)
    {
        if (identity is null || string.IsNullOrWhiteSpace(identity.Username))
            return new { accessLevel = "View" };

        if (string.IsNullOrWhiteSpace(reportDatasetId))
            throw new InvalidOperationException(
                "Effective identity on this token path requires a dataset id (report metadata or PowerBi:SemanticDatasetId / PaginatedDatasetIds).");

        return new
        {
            accessLevel = "View",
            identities = new object[]
            {
                new
                {
                    username = identity.Username.Trim(),
                    roles = identity.Roles.Count > 0 ? identity.Roles.ToArray() : Array.Empty<string>(),
                    datasets = new[] { reportDatasetId.Trim() }
                }
            }
        };
    }

    private static async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
            return;

        var detail = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new HttpRequestException(
            $"Power BI API {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
    }

    private sealed class GenerateTokenV2Request
    {
        [JsonPropertyName("datasets")]
        public List<GenerateTokenV2DatasetRef>? Datasets { get; set; }

        [JsonPropertyName("reports")]
        public List<GenerateTokenV2ReportRef>? Reports { get; set; }

        [JsonPropertyName("identities")]
        public List<GenerateTokenV2Identity>? Identities { get; set; }
    }

    private sealed class GenerateTokenV2Identity
    {
        [JsonPropertyName("username")]
        public required string Username { get; set; }

        [JsonPropertyName("roles")]
        public List<string> Roles { get; set; } = new();

        [JsonPropertyName("datasets")]
        public List<string> Datasets { get; set; } = new();
    }

    private sealed class GenerateTokenV2DatasetRef
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("xmlaPermissions")]
        public required string XmlaPermissions { get; set; }
    }

    private sealed class GenerateTokenV2ReportRef
    {
        [JsonPropertyName("id")]
        public required string Id { get; set; }

        [JsonPropertyName("allowEdit")]
        public bool AllowEdit { get; set; }
    }
}
