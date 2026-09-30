using TabForge.Services;

namespace TabForge;

/// <summary>Update-check regression tests (part of <see cref="SelfTest"/>); no network is used.</summary>
public static partial class SelfTest
{
    private static void TestUpdateCheck()
    {
        // Version order.
        Check("beta is newer than alpha", UpdateService.IsNewer("0.1.0-beta.1", "0.1.0-alpha.6"));
        Check("beta.10 is newer than beta.9 (numeric, not text, order)", UpdateService.IsNewer("0.1.0-beta.10", "0.1.0-beta.9"));
        Check("a final release is newer than its pre-releases", UpdateService.IsNewer("0.1.0", "0.1.0-beta.3"));
        Check("a higher minor version wins over any pre-release", UpdateService.IsNewer("0.2.0-alpha.1", "0.1.0"));
        Check("the same version is not an update", !UpdateService.IsNewer("0.1.0-beta.1", "0.1.0-beta.1"));
        Check("an older release is not an update", !UpdateService.IsNewer("0.1.0-alpha.6", "0.1.0-beta.1"));
        Check("an unparsable version is never treated as newer", !UpdateService.IsNewer("latest", "0.1.0-beta.1"));

        // Reply parsing: the highest valid tag wins, drafts and odd tags are ignored.
        const string reply = """
            [
              { "tag_name": "v0.1.0-alpha.6", "draft": false, "html_url": "https://evil.example/x" },
              { "tag_name": "v0.1.0-beta.2", "draft": false },
              { "tag_name": "v9.9.9", "draft": true },
              { "tag_name": "v0.1.0-beta.3; rm -rf", "draft": false },
              { "tag_name": "v0.1.0-beta.1", "draft": false }
            ]
            """;
        Check("the newest published release is found", UpdateService.LatestVersionFrom(reply) == "0.1.0-beta.2", UpdateService.LatestVersionFrom(reply));
        Check("malformed or non-array replies give no update",
            UpdateService.LatestVersionFrom("{\"message\":\"rate limited\"}") is null && UpdateService.LatestVersionFrom("not json") is null);
        Check("draft releases are ignored", UpdateService.LatestVersionFrom("""[{ "tag_name": "v9.9.9", "draft": true }]""") is null);

        // The page opened is built locally for the validated version, never taken from the reply.
        Check("the download page is on the TabForge GitHub repository",
            UpdateService.PageFor("0.1.0-beta.2").AbsoluteUri == "https://github.com/cobhc95/TabForge/releases/tag/v0.1.0-beta.2");
        Check("an unexpected version falls back to the releases list, never another site",
            UpdateService.PageFor("../../evil").AbsoluteUri == "https://github.com/cobhc95/TabForge/releases");

        // Setting: on by default, visible at the top of Settings > General, bindable.
        Check("update checks are on by default", new AppSettings().General.CheckForUpdates);
        var first = SettingsCatalog.Build(new AppSettings()).First(d => d.Category == "General");
        Check("the update switch is the first row of Settings > General", first.Key == "general.checkupdates", first.Key);
        Check("Check for updates can be bound to a key", HotkeyCatalog.All.Any(a => a.Id == "Help.CheckForUpdates"));
    }
}
