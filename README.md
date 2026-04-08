# Fabric / Power BI embed sample (Blazor Server)

Minimal **.NET 8** app that embeds **two** items from a **Fabric / Power BI** workspace: a **semantic (Power BI) report** and a **paginated (RDL) report**. Tokens are generated **only on the server** using a **service principal** (app registration + client secret).

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Azure AD **app registration** with a **client secret** (the **Value**, not the Secret **ID**)
- **Power BI / Fabric**: tenant allows **service principals**; the app is **added to the workspace** with access to the reports
- Optional: [HTTPS dev certificate](https://learn.microsoft.com/aspnet/core/security/enforcing-ssl#trust-the-aspnet-core-https-development-certificate-on-windows-and-macos) trusted locally (`dotnet dev-certs https --trust`)

## Run

```bash
cd FabricEmbedSample
dotnet restore
dotnet run --launch-profile https
```

Open **https://localhost:7288** (HTTP fallback: http://localhost:5288). Use the nav links for **Semantic report** and **Paginated report**.

## Configuration

**Do not commit secrets.** Use [user secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets) (already wired via `UserSecretsId` in the `.csproj`) or environment variables in production.

```bash
dotnet user-secrets set "PowerBi:TenantId" "<directory-tenant-guid>"
dotnet user-secrets set "PowerBi:ClientId" "<app-client-guid>"
dotnet user-secrets set "PowerBi:ClientSecret" "<secret-value-from-portal>"
dotnet user-secrets set "PowerBi:WorkspaceId" "<workspace-guid>"
dotnet user-secrets set "PowerBi:SemanticReportId" "<report-guid>"
dotnet user-secrets set "PowerBi:PaginatedReportId" "<report-guid>"
```

Optional:

| Key | When |
|-----|------|
| `PowerBi:SemanticDatasetId` | If **GET report** does not return `datasetId` but you still need **Generate Token V2** (e.g. DirectLake). Usually omitted if the API returns `datasetId`. |
| `PowerBi:PaginatedDatasetIds` | JSON array of dataset GUIDs if the paginated report uses **semantic models** and **V2** token is required. Example in `appsettings.json`. |

IDs should be **plain GUIDs** (no `<>`, no query strings). The app normalizes common copy-paste mistakes.

## What this sample demonstrates

- **MSAL** confidential client (singleton) + **`IHttpClientFactory`** for Power BI REST
- **Semantic / DirectLake**: **Generate Token V2** when a dataset id is available (V1 is not valid for DirectLake)
- **Paginated**: V1 or V2 depending on config; JS uses a **heuristic** for readiness (paginated embed does not support `loaded` / `rendered` per Microsoft)
- Blazor: embed container **height + iframe** layout; **`@key`** on viewers when switching routes

## API (local)

`GET /api/embed-config?kind=Semantic` or `kind=Paginated` returns JSON with `embedToken`, `embedUrl`, and server **timings** (for debugging).

## References

- [Embed content with service principal](https://learn.microsoft.com/power-bi/developer/embedded/embed-service-principal)
- [Embed a paginated report](https://learn.microsoft.com/power-bi/paginated-reports/paginated-reports-embed)
- [Generate token (REST)](https://learn.microsoft.com/rest/api/power-bi/embed-token/generate-token)

## License

Use and adapt freely for your projects; ensure compliance with Microsoft Power BI / Fabric terms for your tenant.
