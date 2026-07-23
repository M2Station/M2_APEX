namespace Listly.Models;

/// <summary>
/// A default quick-pick link — a folder / UNC path or URL, with optional launch arguments — loaded from
/// the embedded <c>Assets/default-link-list.json</c>. This one shared file seeds BOTH M2_APEX's search
/// quick picks (<see cref="SearchLink"/>) and M2 Commander's drives-view quick links (<see cref="CommanderLink"/>),
/// so editing it updates the defaults on both sides without touching code.
/// </summary>
public sealed class SupportLink
{
    public string Name { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Arguments { get; set; } = string.Empty;

    private static List<SupportLink>? _catalog;

    /// <summary>The link catalog from <c>Assets/default-link-list.json</c> (cached, never null).</summary>
    public static IReadOnlyList<SupportLink> Catalog =>
        _catalog ??= EmbeddedCatalog.Load<SupportLink>("default-link-list.json");
}
