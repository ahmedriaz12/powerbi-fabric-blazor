namespace FabricEmbedSample.Models;

/// <summary>Maps to embed token <c>identities[].username</c> / <c>roles</c> (from API query when enabled).</summary>
public sealed class EffectiveIdentityInput
{
    public required string Username { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = Array.Empty<string>();
}
