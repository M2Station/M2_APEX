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

    // Separate client that does NOT auto-follow redirects, so the release-page fallback can
    // read the Location header of github.com/.../releases/latest.
    private static readonly HttpClient Redirectless = CreateRedirectlessClient();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        // GitHub's API rejects requests without a User-Agent.
        http.DefaultRequestHeaders.UserAgent.ParseAdd("M2_APEX-Updater");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return http;
    }

    private static HttpClient CreateRedirectlessClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("M2_APEX-Updater");
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
        return await CheckForUpdateAsync(Owner, Repo, CurrentVersion, ct);
    }

    /// <summary>
    /// Queries the latest release of any <paramref name="owner"/>/<paramref name="repo"/> (e.g. a sibling
    /// M2 app) and reports whether it is newer than <paramref name="currentVersion"/>. Returns <c>null</c>
    /// on any network/parse failure. Used by the Settings quick-picks per-app "Check Update".
    /// </summary>
    public static async Task<UpdateInfo?> CheckForUpdateAsync(string owner, string repo, string currentVersion, CancellationToken ct = default)
    {
        string current = currentVersion;
        string releasesUrl = $"https://github.com/{owner}/{repo}/releases/latest";

        try
        {
            var apiUrl = $"https://api.github.com/repos/{owner}/{repo}/releases/latest";
            using var response = await Http.GetAsync(apiUrl, ct);

            // No releases published yet — treat as "up to date" rather than an error.
            if (response.StatusCode == HttpStatusCode.NotFound)
                return new UpdateInfo(false, current, current, "", releasesUrl, null, null);

            // Rate-limited or otherwise unavailable. Unauthenticated api.github.com allows only
            // 60 requests/hour per IP, which a shared corporate NAT exhausts quickly (HTTP 403).
            // Fall back to the github.com /releases/latest redirect, which is not API-rate-limited.
            if (!response.IsSuccessStatusCode)
                return await CheckViaReleasesRedirectAsync(owner, repo, current, releasesUrl, ct);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;

            string tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
            string latest = tag.TrimStart('v', 'V');
            bool newer = IsNewer(latest, current);

            string notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
            string htmlUrl = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
            if (!IsTrustedReleaseUrl(htmlUrl, owner, repo))
                htmlUrl = releasesUrl;

            (string? dlUrl, string? dlName) = newer && root.TryGetProperty("assets", out var assets)
                ? PickAsset(assets, owner, repo)
                : (null, null);

            return new UpdateInfo(newer, current, string.IsNullOrEmpty(latest) ? current : latest,
                notes, htmlUrl, dlUrl, dlName);
        }
        catch
        {
            // Offline, timeout, or malformed API response: try the redirect fallback before giving up.
            return await CheckViaReleasesRedirectAsync(owner, repo, current, releasesUrl, ct);
        }
    }

    /// <summary>
    /// Resolves the latest release version without the rate-limited REST API by following the
    /// <c>github.com/{owner}/{repo}/releases/latest</c> redirect: GitHub answers with a 302 whose
    /// <c>Location</c> points at <c>/releases/tag/&lt;tag&gt;</c>. Returns <c>null</c> when there is no
    /// published release or the request fails. Assets aren't enumerated here, so the caller opens the
    /// release page to download.
    /// </summary>
    private static async Task<UpdateInfo?> CheckViaReleasesRedirectAsync(
        string owner, string repo, string current, string releasesUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, releasesUrl);
            using var response = await Redirectless.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

            // A published release redirects to its tag page; anything else means "can't tell".
            if ((int)response.StatusCode is < 300 or >= 400)
                return null;

            string location = response.Headers.Location?.ToString() ?? "";
            const string marker = "/releases/tag/";
            int idx = location.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                return null;

            string tag = location[(idx + marker.Length)..].Trim('/');
            string latest = tag.TrimStart('v', 'V');
            if (string.IsNullOrEmpty(latest))
                return null;

            bool newer = IsNewer(latest, current);

            string tagUrl = location.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? location
                : $"https://github.com{(location.StartsWith('/') ? "" : "/")}{location}";
            if (!IsTrustedReleaseUrl(tagUrl, owner, repo))
                tagUrl = releasesUrl;

            return new UpdateInfo(newer, current, latest, "", tagUrl, null, null);
        }
        catch
        {
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

    /// <summary>Opens a trusted release/download URL for the given <paramref name="owner"/>/<paramref name="repo"/>
    /// in the default browser, falling back to that repo's latest-release page.</summary>
    public static void OpenInBrowser(string? url, string owner, string repo)
    {
        if (string.IsNullOrEmpty(url) || (!IsTrustedReleaseUrl(url, owner, repo) && !IsTrustedAssetUrl(url, owner, repo)))
            url = $"https://github.com/{owner}/{repo}/releases/latest";

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Best effort.
        }
    }

    /// <summary>Opens a repo's latest-release page in the browser (used by the Settings "Install" button).</summary>
    public static void OpenReleasesPage(string owner, string repo) =>
        OpenInBrowser(null, owner, repo);

    /// <summary>Reads an installed executable's product/file version, or "0.0.0" when it can't be read.</summary>
    public static string GetInstalledVersion(string? exePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(exePath) || !System.IO.File.Exists(exePath))
                return "0.0.0";

            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
            var version = info.ProductVersion ?? info.FileVersion;
            return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        }
        catch
        {
            return "0.0.0";
        }
    }

    // Pick the .exe asset matching this machine's CPU architecture, preferring the installer
    // (Setup-*.exe) over the portable build. Falls back to any .exe when no arch-specific match.
    private static (string? url, string? name) PickAsset(JsonElement assets, string owner, string repo)
    {
        if (assets.ValueKind != JsonValueKind.Array)
            return (null, null);

        bool wantArm = RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

        (string? Url, string? Name) arch = default;   // any build for this architecture
        (string? Url, string? Name) setup = default;  // preferred: the installer for this architecture
        (string? Url, string? Name) any = default;    // last resort: any .exe

        foreach (var asset in assets.EnumerateArray())
        {
            string name = asset.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() ?? "" : "";

            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !IsTrustedAssetUrl(url, owner, repo))
                continue;

            if (any.Url is null)
                any = (url, name);

            bool isArm = name.Contains("arm64", StringComparison.OrdinalIgnoreCase);
            bool archMatch = wantArm
                ? isArm
                : name.Contains("x64", StringComparison.OrdinalIgnoreCase) && !isArm;
            if (!archMatch)
                continue;

            if (arch.Url is null)
                arch = (url, name);

            if (setup.Url is null && name.Contains("setup", StringComparison.OrdinalIgnoreCase))
                setup = (url, name);
        }

        var pick = setup.Url is not null ? setup : arch.Url is not null ? arch : any;
        return (pick.Url, pick.Name);
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

    private static bool IsTrustedReleaseUrl(string url) => IsTrustedReleaseUrl(url, Owner, Repo);

    private static bool IsTrustedReleaseUrl(string url, string owner, string repo) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.StartsWith($"/{owner}/{repo}/", StringComparison.OrdinalIgnoreCase);

    private static bool IsTrustedAssetUrl(string url) => IsTrustedAssetUrl(url, Owner, Repo);

    private static bool IsTrustedAssetUrl(string url, string owner, string repo) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) &&
        u.Scheme == Uri.UriSchemeHttps &&
        u.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) &&
        u.AbsolutePath.StartsWith($"/{owner}/{repo}/releases/download/", StringComparison.OrdinalIgnoreCase);
}
