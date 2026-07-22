using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace Listly.Services;

/// <summary>
/// Checks this repository's GitHub Releases for a newer version, built directly on the
/// public Releases API — no extra update server. Modeled on the M2_GIT_DIFF updater:
/// query the latest release, compare it to the running build, and (when newer) surface
/// the arch-matched download so the user can grab it from the Releases page.
/// </summary>
public static class UpdateService
{
    /// <summary>The GitHub owner/repo the releases are published under.</summary>
    public const string Owner = "M2Station";
    public const string Repo = "M2_APEX";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("M2_APEX-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    /// <summary>The running app version as "X.Y.Z" (e.g. "0.0.1").</summary>
    public static string CurrentVersion
    {
        get
        {
            var info = Assembly.GetEntryAssembly()?
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrEmpty(info))
            {
                // Strip any "+<gitsha>" build metadata SourceLink may append.
                int plus = info.IndexOf('+');
                return plus >= 0 ? info[..plus] : info;
            }

            var v = Assembly.GetEntryAssembly()?.GetName().Version;
            return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    /// <summary>Outcome of an update check. Safe to show directly in the UI.</summary>
    public sealed record UpdateInfo(
        bool HasUpdate,
        string CurrentVersion,
        string LatestVersion,
        string Notes,
        string ReleaseUrl,
        string? DownloadUrl,
        string? DownloadName);

    /// <summary>
    /// Queries the latest release and reports whether it is newer than the running build.
    /// Returns <c>null</c> on any network/parse failure so the caller can treat it as
    /// "couldn't check" rather than crashing or nagging.
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken ct = default)
    {
        string current = CurrentVersion;
        string releasesUrl = $"https://github.com/{Owner}/{Repo}/releases/latest";

        try
        {
            var apiUrl = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
            using var response = await Http.GetAsync(apiUrl, ct);

            // No releases published yet — treat as "up to date" rather than an error.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateInfo(false, current, current, "", releasesUrl, null, null);

            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string latest = tag.TrimStart('v', 'V');
            bool newer = IsNewer(latest, current);

            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            if (!IsTrustedReleaseUrl(htmlUrl))
                htmlUrl = releasesUrl;

            (string? dlUrl, string? dlName) = newer && root.TryGetProperty("assets", out var assets)
                ? PickAsset(assets)
                : (null, null);

            return new UpdateInfo(newer, current, string.IsNullOrEmpty(latest) ? current : latest,
                notes, htmlUrl, dlUrl, dlName);
        }
        catch
        {
            // Offline, timeout, or malformed response: quietly report "couldn't check".
            return null;
        }
    }

    /// <summary>Opens a trusted GitHub release/download URL in the default browser.</summary>
    public static void OpenInBrowser(string? url)
    {
        if (string.IsNullOrEmpty(url) || (!IsTrustedReleaseUrl(url) && !IsTrustedAssetUrl(url)))
            url = $"https://github.com/{Owner}/{Repo}/releases/latest";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // Best effort; nothing else to do if the shell refuses.
        }
    }

    // Pick the single-file .exe asset matching this machine's CPU architecture. Falls
    // back to the first .exe when no arch-specific build is found.
    private static (string? url, string? name) PickAsset(JsonElement assets)
    {
        if (assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        bool wantArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        string? fallbackUrl = null, fallbackName = null;
        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !IsTrustedAssetUrl(url))
                continue;

            bool isArm = name.Contains("arm64", StringComparison.OrdinalIgnoreCase);
            bool matches = wantArm
                ? isArm
                : name.Contains("x64", StringComparison.OrdinalIgnoreCase) && !isArm;

            if (matches)
                return (url, name);

            fallbackUrl ??= url;
            fallbackName ??= name;
        }

        return (fallbackUrl, fallbackName);
    }

    // True when "latest" is a strictly higher X.Y.Z version than "current".
    private static bool IsNewer(string latest, string current)
    {
        int[] a = ParseVersion(latest);
        int[] b = ParseVersion(current);
        for (int i = 0; i < 3; i++)
        {
            if (a[i] > b[i]) return true;
            if (a[i] < b[i]) return false;
        }
        return false;
    }

    // Parse "v1.2.3" / "1.2.3-beta" into [major, minor, patch]; suffixes are dropped.
    private static int[] ParseVersion(string v)
    {
        string[] core = (v ?? "").Trim().TrimStart('v', 'V').Split('-')[0].Split('.');
        int Part(int i) => i < core.Length && int.TryParse(core[i], out int num) ? num : 0;
        return new[] { Part(0), Part(1), Part(2) };
    }

    private static bool IsTrustedReleaseUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.StartsWith($"/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedAssetUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.StartsWith($"/{Owner}/{Repo}/releases/download/", StringComparison.OrdinalIgnoreCase);
}
