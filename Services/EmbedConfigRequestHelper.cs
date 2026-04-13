using FabricEmbedSample.Models;
using Microsoft.Extensions.Hosting;

namespace FabricEmbedSample.Services;

/// <summary>
/// Shared logic for the embed-config minimal API and the Blazor report viewer (no in-process HTTP round-trip).
/// </summary>
public static class EmbedConfigRequestHelper
{
    public static bool TryParseKind(string? kind, out EmbedReportKind reportKind, out string? badRequestMessage)
    {
        reportKind = default;
        badRequestMessage = null;
        if (!Enum.TryParse<EmbedReportKind>(kind, ignoreCase: true, out reportKind))
        {
            badRequestMessage = "Invalid kind. Use Semantic or Paginated.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// When <paramref name="effectiveUsername"/> is empty, returns <c>true</c> with <paramref name="identity"/> <c>null</c>.
    /// </summary>
    public static bool TryBuildEffectiveIdentity(
        string? effectiveUsername,
        string? effectiveRoles,
        IHostEnvironment env,
        PowerBiOptions options,
        out EffectiveIdentityInput? identity,
        out string? badRequestMessage)
    {
        identity = null;
        badRequestMessage = null;

        if (string.IsNullOrWhiteSpace(effectiveUsername))
            return true;

        if (!env.IsDevelopment() && !options.EnableEffectiveIdentityTest)
        {
            badRequestMessage =
                "effectiveUsername is only allowed when Environment=Development or PowerBi:EnableEffectiveIdentityTest=true.";
            return false;
        }

        string[] roleList = Array.Empty<string>();
        if (!string.IsNullOrWhiteSpace(effectiveRoles))
        {
            roleList = effectiveRoles
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        identity = new EffectiveIdentityInput
        {
            Username = effectiveUsername.Trim(),
            Roles = roleList
        };
        return true;
    }

    public static string FormatEmbedExceptionMessage(Exception ex)
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

        return detail;
    }
}
