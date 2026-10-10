using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Authentication;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace TabForge.Services;

/// <summary>A newer TabForge release: its version and the release page built for it (never taken from the reply).</summary>
public sealed record ReleaseInfo(string Version, Uri Page);

// Owns: the update check request to the release feed with its timeouts and size caps.
// Does not own: when the check runs (UpdateCheckController) and the dialogs.
// Tests: TestUpdateCheck.
/// <summary>
/// Update check, deliberately minimal:
/// - one anonymous HTTPS GET to a fixed address (the public GitHub releases list of this repository);
///   TLS 1.2/1.3 with normal certificate validation, no redirects, no cookies, no credentials, no proxy
///   auto-configuration scripts beyond the system proxy, 10 s timeout, 256 KB response cap;
/// - nothing is sent except the User-Agent GitHub requires ("TabForge/&lt;version&gt;");
/// - only the "tag_name" and "draft" fields are read, and a tag must be a plain version (v1.2.3, optionally with a pre-release suffix);
///   no text, links or files from the reply are shown, opened or saved;
/// - the page offered to open is built from constants + that validated version, on github.com/cobhc95/TabForge;
/// - nothing is ever downloaded or installed by TabForge; the user downloads from GitHub in their browser.
/// With "Check for updates automatically" off, none of this code runs unless Help > Check for updates is used.
/// </summary>
public static class UpdateService
{
    public const string Repository = "cobhc95/TabForge";
    private static readonly Uri ReleasesApi = new($"https://api.github.com/repos/{Repository}/releases?per_page=20");
    private const int MaxResponseBytes = 256 * 1024;
    private const int MaxReleasesRead = 20;
    private static readonly Regex TagPattern = new(@"^v?(\d{1,4}\.\d{1,4}\.\d{1,4}(?:-[0-9A-Za-z]{1,16}(?:\.[0-9A-Za-z]{1,16}){0,3})?)$",
        RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(50));

    /// <summary>The release page for a validated version (built locally, never from the network reply).</summary>
    public static Uri PageFor(string version) =>
        TagPattern.IsMatch(version) ? new Uri($"https://github.com/{Repository}/releases/tag/v{version.TrimStart('v', 'V')}")
            : new Uri($"https://github.com/{Repository}/releases");

    /// <summary>A release newer than <paramref name="currentVersion"/>, or null (none, offline, refused or unexpected reply).</summary>
    public static async Task<ReleaseInfo?> CheckAsync(string currentVersion, CancellationToken cancellation)
    {
        var json = await FetchAsync(cancellation).ConfigureAwait(false);
        // A final-release user is only offered final releases; a pre-release user is offered anything newer.
        var latest = json is null ? null : LatestVersionFrom(json, includePreReleases: currentVersion.Contains('-') || AppInfo.IsPreRelease);
        return latest is not null && IsNewer(latest, currentVersion) ? new ReleaseInfo(latest, PageFor(latest)) : null;
    }

    private static async Task<string?> FetchAsync(CancellationToken cancellation)
    {
        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            Credentials = null,
            PreAuthenticate = false,
            AutomaticDecompression = DecompressionMethods.None,
            MaxResponseHeadersLength = 64, // KB
            MaxConnectionsPerServer = 1,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            SslOptions = { EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13 },
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10), MaxResponseContentBufferSize = MaxResponseBytes };
        using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesApi);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("TabForge", SafeToken(AppInfo.Version)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellation).ConfigureAwait(false);
        if (response.StatusCode != HttpStatusCode.OK) return null;
        if (response.Content.Headers.ContentType?.MediaType is not ("application/json" or "application/vnd.github+json")) return null;
        if (response.Content.Headers.ContentLength is > MaxResponseBytes) return null;
        await using var stream = await response.Content.ReadAsStreamAsync(cancellation).ConfigureAwait(false);
        var buffer = new MemoryStream();
        var chunk = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellation).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes) return null; // bounded even when the length header lies
            buffer.Write(chunk, 0, read);
        }
        return System.Text.Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static string SafeToken(string version) => Regex.Replace(version, "[^0-9A-Za-z.\\-]", "");

    /// <summary>Highest plain-version tag among the first published (non-draft) releases of a GitHub releases array.</summary>
    public static string? LatestVersionFrom(string json, bool includePreReleases = true)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            string? best = null;
            foreach (var release in doc.RootElement.EnumerateArray().Take(MaxReleasesRead))
            {
                if (release.ValueKind != JsonValueKind.Object) continue;
                if (release.TryGetProperty("draft", out var draft) && draft.ValueKind != JsonValueKind.False) continue;
                if (!release.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String) continue;
                var match = TagPattern.Match(tag.GetString() ?? "");
                if (!match.Success) continue;
                var version = match.Groups[1].Value;
                if (!includePreReleases && (version.Contains('-') || (release.TryGetProperty("prerelease", out var pre) && pre.ValueKind == JsonValueKind.True))) continue;
                if (best is null || Compare(version, best) > 0) best = version;
            }
            return best;
        }
        catch (Exception ex) when (ex is JsonException or RegexMatchTimeoutException) { return null; } // Not logged: update check: offline is silent by design
    }

    public static bool IsNewer(string candidate, string current) => Compare(candidate, current) > 0;

    /// <summary>Semantic-version order: 1.2.0-alpha.6 &lt; 1.2.0-alpha.10 &lt; 1.2.0 &lt; 1.3.0.</summary>
    public static int Compare(string a, string b)
    {
        if (!TryParse(a, out var x) || !TryParse(b, out var y)) return 0; // unknown formats are never "newer"
        for (var i = 0; i < 3; i++)
            if (x.Core[i] != y.Core[i]) return x.Core[i].CompareTo(y.Core[i]);
        if (x.Pre.Length == 0 && y.Pre.Length == 0) return 0;
        if (x.Pre.Length == 0) return 1;  // a final release is newer than its pre-releases
        if (y.Pre.Length == 0) return -1;
        for (var i = 0; i < Math.Min(x.Pre.Length, y.Pre.Length); i++)
        {
            var aNum = int.TryParse(x.Pre[i], out var an); var bNum = int.TryParse(y.Pre[i], out var bn);
            var c = aNum && bNum ? an.CompareTo(bn) : aNum ? -1 : bNum ? 1 : string.CompareOrdinal(x.Pre[i], y.Pre[i]);
            if (c != 0) return c;
        }
        return x.Pre.Length.CompareTo(y.Pre.Length);
    }

    private static bool TryParse(string text, out (int[] Core, string[] Pre) version)
    {
        version = (Array.Empty<int>(), Array.Empty<string>());
        var main = text.TrimStart('v', 'V').Split('+')[0];
        var dash = main.IndexOf('-');
        var core = (dash < 0 ? main : main[..dash]).Split('.');
        if (core.Length != 3) return false;
        var numbers = new int[3];
        for (var i = 0; i < 3; i++)
            if (!int.TryParse(core[i], System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out numbers[i])) return false;
        var pre = dash < 0 ? Array.Empty<string>() : main[(dash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        version = (numbers, pre);
        return true;
    }
}
