using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using FabricEmbedSample.Components;
using FabricEmbedSample.Models;
using FabricEmbedSample.Services;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<PowerBiOptions>()
    .Bind(builder.Configuration.GetSection(PowerBiOptions.SectionName))
    .PostConfigure(static o => o.Normalize())
    .Validate(o => !string.IsNullOrWhiteSpace(o.TenantId), "PowerBi:TenantId is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientId), "PowerBi:ClientId is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.ClientSecret), "PowerBi:ClientSecret is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.WorkspaceId), "PowerBi:WorkspaceId is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.SemanticReportId), "PowerBi:SemanticReportId is required.")
    .Validate(o => !string.IsNullOrWhiteSpace(o.PaginatedReportId), "PowerBi:PaginatedReportId is required.")
    .ValidateOnStart();

// Single MSAL confidential client per process so in-memory token cache is reused (client credentials).
// Options are validated on start; factory receives valid PowerBiOptions.
builder.Services.AddSingleton<IConfidentialClientApplication>(sp =>
{
    var o = sp.GetRequiredService<IOptions<PowerBiOptions>>().Value;
    return ConfidentialClientApplicationBuilder.Create(o.ClientId)
        .WithClientSecret(o.ClientSecret)
        .WithTenantId(o.TenantId)
        .Build();
});

// Pooled HttpClients; Authorization is set per HttpRequestMessage (do not mutate shared DefaultRequestHeaders per user/token).
builder.Services.AddHttpClient(PowerBiEmbedService.PowerBiHttpClientName, (sp, client) =>
{
    var o = sp.GetRequiredService<IOptions<PowerBiOptions>>().Value;
    client.BaseAddress = new Uri($"{o.ApiHost.TrimEnd('/')}/v1.0/myorg/");
    client.DefaultRequestHeaders.Accept.Clear();
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
});

builder.Services.AddSingleton<PowerBiEmbedService>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapGet("/api/embed-config", async (
    string kind,
    string? effectiveUsername,
    string? effectiveRoles,
    PowerBiEmbedService svc,
    IHostEnvironment env,
    IOptions<PowerBiOptions> options,
    CancellationToken ct) =>
{
    if (!EmbedConfigRequestHelper.TryParseKind(kind, out var k, out var kindError))
        return Results.BadRequest(new { error = kindError });

    if (!EmbedConfigRequestHelper.TryBuildEffectiveIdentity(
            effectiveUsername,
            effectiveRoles,
            env,
            options.Value,
            out var identity,
            out var identityError))
        return Results.BadRequest(new { error = identityError });

    try
    {
        var dto = await svc.GetEmbedConfigAsync(k, identity, ct).ConfigureAwait(false);
        return Results.Ok(dto);
    }
    catch (Exception ex)
    {
        var detail = EmbedConfigRequestHelper.FormatEmbedExceptionMessage(ex);
        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
