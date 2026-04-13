# Fabric / Power BI embed sample (Blazor Server)

Minimal **.NET 8** app that embeds **two** items from a **Fabric / Power BI** workspace: a **semantic (Power BI) report** and a **paginated (RDL) report**. Tokens are generated **only on the server** using a **service principal** (app registration + client secret).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Azure AD **app registration** with a **client secret** (the **Value**, not the Secret **ID**)
- **Power BI / Fabric**: tenant allows **service principals**; the app is **added to the workspace** with access to the reports
- Optional: [HTTPS dev certificate](https://learn.microsoft.com/aspnet/core/security/enforcing-ssl#trust-the-aspnet-core-https-development-certificate-on-windows-and-macos) trusted locally (`dotnet dev-certs https --trust`)

## Run

```bash
cd powerbi-fabric-blazor
dotnet restore
dotnet run --launch-profile https
```

Open **https://localhost:7288** (HTTP fallback: http://localhost:5288). Use the nav links for **Semantic report** and **Paginated report**.

## Configuration

**Do not commit secrets.** Use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (already wired via `UserSecretsId` in the `.csproj`) or environment variables in production.

Required Power BI settings (`TenantId`, `ClientId`, `ClientSecret`, `WorkspaceId`, `SemanticReportId`, `PaginatedReportId`) are **validated when the app starts** (`ValidateOnStart`). If something is missing or blank after `Normalize()`, the process exits with `Microsoft.Extensions.Options.OptionsValidationException` (e.g. *PowerBi:ClientSecret is required.*). This is expected until you configure user-secrets or environment variables — the app will not listen for HTTP until validation passes.

**Troubleshooting:** If startup fails with `OptionsValidationException` (e.g. *PowerBi:ClientSecret is required.*), configure [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) for all required keys:

```bash
dotnet user-secrets set "PowerBi:TenantId" "<directory-tenant-guid>"
dotnet user-secrets set "PowerBi:ClientId" "<app-client-guid>"
dotnet user-secrets set "PowerBi:ClientSecret" "<secret-value-from-portal — Value, not Secret ID>"
dotnet user-secrets set "PowerBi:WorkspaceId" "<workspace-guid>"
dotnet user-secrets set "PowerBi:SemanticReportId" "<report-guid>"
dotnet user-secrets set "PowerBi:PaginatedReportId" "<report-guid>"
```

Optional:

| Key | When |
|-----|------|
| `PowerBi:SemanticDatasetId` | If **GET report** does not return `datasetId` but you still need **Generate Token V2** (e.g. DirectLake). Usually omitted if the API returns `datasetId`. |
| `PowerBi:PaginatedDatasetIds` | JSON array of dataset GUIDs if the paginated report uses **semantic models** and **V2** token is required. Example in `appsettings.json`. |
| `PowerBi:EnableEffectiveIdentityTest` | When `true`, allows `effectiveUsername` / `effectiveRoles` on `/api/embed-config` outside Development (e.g. staging). Default `false`. |

IDs should be **plain GUIDs** (no `<>`, no query strings). The app normalizes common copy-paste mistakes.

## Row-level security (RLS) and effective identity

The service principal has no end-user context by default. For dataset RLS, the embed token can include an **effective identity** (`identities[]` on the Generate Token call): a `username` string, optional `roles`, and the dataset GUIDs that identity applies to. In the model, `USERNAME()` / `USERPRINCIPALNAME()` resolve to that `username` inside role filters—so whatever your app puts in the token must match how you wrote the DAX (portal id, email, etc.).

Semantic and paginated reports both consume the same idea when they hit a **Power BI dataset**: rules are on the model. If an RDL talks to SQL (or something else) directly, you handle filtering there or via report parameters instead.

Typical pattern: one role in the dataset with something like `[UserKey] = USERNAME()`, publish, then have your API set `username` from the signed-in user (from your IdP claims) when generating the token. You usually do **not** create one role per user; you pass different `username` values per request. If the token includes `roles`, names must match roles defined on the dataset.

### Testing in this repo

**Semantic report** and **Paginated report** each include the same dev-only inputs (test `username`, optional role names, **Apply & reload embed**). That only works when `ASPNETCORE_ENVIRONMENT=Development` or `PowerBi:EnableEffectiveIdentityTest` is `true` (see table above). The viewer shows what the server sent so you can compare it to your model—**in production, derive the user from server-side auth**, not from a client field.

Some datasets (notably certain DirectLake / Fabric setups) return **403** with *“Creating embed token with effective identity is not supported for this datasource”*. That comes from the Power BI API, not a bug in this app; check Microsoft’s current guidance for your storage mode and consider validating with a small imported model if you need to prove the flow end-to-end.

**Waiting on tenant details:** If you see that 403, confirm the semantic model’s **storage mode** (Import, DirectQuery, Direct Lake, composite) and lineage. Once that’s known, the next step is to match **embed + RLS** to what that engine supports (effective identity, alternate token shape, or filtering outside the dataset).

## What this sample demonstrates

- MSAL confidential client + `IHttpClientFactory` for Power BI REST
- Required `PowerBi` options validated **at startup** (`ValidateOnStart`) so misconfiguration fails fast with a clear message
- Blazor report viewer calls `PowerBiEmbedService` **in-process** (no loopback `HttpClient` to `/api/embed-config`)
- Semantic: Generate Token V2 when a dataset id is available (required for DirectLake)
- Paginated: V1 or V2 depending on config; JS uses a short timer for readiness (paginated embed doesn’t expose reliable `rendered` events)
- Optional RLS hook: `identities` on the token when testing from `/api/embed-config` plus **Semantic** and **Paginated** pages
- Layout: fixed-height embed host and iframe fill

## API (local)

`GET /api/embed-config?kind=Semantic|Paginated` returns `embedToken`, `embedUrl`, timings, and `tokenMode`.

For RLS testing (Development or `EnableEffectiveIdentityTest`): add `effectiveUsername` and optionally `effectiveRoles` (comma-separated). The JSON then includes `effectiveIdentity` echoing what went into `identities[]` so you can verify against your dataset.

## References

- [Embed content with service principal](https://learn.microsoft.com/power-bi/developer/embedded/embed-service-principal)
- [Embed a paginated report](https://learn.microsoft.com/power-bi/paginated-reports/paginated-reports-embed)
- [Generate token (REST)](https://learn.microsoft.com/rest/api/power-bi/embed-token/generate-token)

## Verify a clean setup

From an empty folder, clone the repo, configure user secrets, run:

```bash
git clone https://github.com/ahmedriaz12/powerbi-fabric-blazor.git
cd powerbi-fabric-blazor
dotnet restore
dotnet user-secrets set "PowerBi:TenantId" "<guid>"
# ... remaining user-secrets keys as in Configuration above ...
dotnet run --launch-profile https
```

Then open `/semantic` and `/paginated`. If `dotnet user-secrets` is skipped, the app will fail at runtime with missing Power BI configuration—that is expected.

## License

Use and adapt freely for your projects; ensure compliance with Microsoft Power BI / Fabric terms for your tenant.
