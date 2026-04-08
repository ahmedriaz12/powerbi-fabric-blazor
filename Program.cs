using System.Net.Http.Headers;
using System.Text.Json.Serialization;
using FabricEmbedSample.Components;
using FabricEmbedSample.Models;
using FabricEmbedSample.Services;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<PowerBiOptions>(
    builder.Configuration.GetSection(PowerBiOptions.SectionName));
builder.Services.PostConfigure<PowerBiOptions>(static o => o.Normalize());

// Single MSAL confidential client per process so in-memory token cache is reused (client credentials).
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

builder.Services.AddScoped<HttpClient>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseAntiforgery();
app.UseStaticFiles();

app.MapGet("/api/embed-config", async (string kind, PowerBiEmbedService svc, CancellationToken ct) =>
{
    if (!Enum.TryParse<EmbedReportKind>(kind, ignoreCase: true, out var k))
        return Results.BadRequest(new { error = "Invalid kind. Use Semantic or Paginated." });

    try
    {
        var dto = await svc.GetEmbedConfigAsync(k, ct).ConfigureAwait(false);
        return Results.Ok(dto);
    }
    catch (Exception ex)
    {
        var detail = ex.Message;
        if (detail.Contains("AADSTS7000215", StringComparison.Ordinal) ||
            detail.Contains("Invalid client secret", StringComparison.OrdinalIgnoreCase))
        {
            detail +=
                " — Fix: In Azure Portal → Microsoft Entra ID → App registrations → your app → Certificates & secrets: " +
                "create a new client secret and copy the Value (password) shown once at creation, not the Secret ID column. " +
                "dotnet user-secrets set \"PowerBi:ClientSecret\" \"<paste Value here>\"";
        }

        return Results.Problem(
            detail: detail,
            statusCode: StatusCodes.Status502BadGateway);
    }
});

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
