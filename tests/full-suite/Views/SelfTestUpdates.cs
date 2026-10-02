using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Services;
using TabForge.Views;

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

        // 0.5 is the first final release: tag v0.5.0 equals version 0.5.0; a final-release user is offered 0.5.1 / 0.6.0 but never betas.
        Check("tag v0.5.0 equals version 0.5.0", UpdateService.Compare("v0.5.0", "0.5.0") == 0 && !UpdateService.IsNewer("v0.5.0", "0.5.0"));
        Check("0.5.1 and 0.6.0 are newer than 0.5.0", UpdateService.IsNewer("v0.5.1", "0.5.0") && UpdateService.IsNewer("v0.6.0", "0.5.0"));
        const string finalReply = """
            [
              { "tag_name": "v0.6.0-beta.1", "draft": false, "prerelease": true },
              { "tag_name": "v0.5.1", "draft": false, "prerelease": false },
              { "tag_name": "v0.5.2", "draft": false, "prerelease": true },
              { "tag_name": "v0.5.0", "draft": false }
            ]
            """;
        Check("a final-release user is not offered betas or flagged pre-releases", UpdateService.LatestVersionFrom(finalReply, includePreReleases: false) == "0.5.1", UpdateService.LatestVersionFrom(finalReply, includePreReleases: false));
        Check("a pre-release user is offered the newest of all", UpdateService.LatestVersionFrom(finalReply, includePreReleases: true) == "0.6.0-beta.1");
        Check("display version: 0.5.0 shows as 0.5", AppInfo.FormatDisplay("0.5.0", false) == "0.5");
        Check("display version: 0.5.1 shows in full", AppInfo.FormatDisplay("0.5.1", false) == "0.5.1");
        Check("display version: betas and flagged versions say pre-release",
            AppInfo.FormatDisplay("0.6.0-beta.1", false) == "0.6.0 beta.1 (pre-release)" && AppInfo.FormatDisplay("0.5.0", true) == "0.5.0 (pre-release)");
        Check("this build is the official release: no pre-release flag or text", !AppInfo.IsPreRelease && !AppInfo.DisplayVersion.Contains("pre-release"), AppInfo.DisplayVersion);

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
        TestUpdateCheckNowAndVersionLabel();
    }

    /// <summary>Settings > General > Updates > Check now, and the version label in the Settings bottom bar (light and dark, 1.0 and 1.5 zoom).</summary>
    private static void TestUpdateCheckNowAndVersionLabel()
    {
        var rows = SettingsCatalog.Build(new AppSettings()).Where(d => d.Category == SettingsCatalog.General && d.Group == "Updates").Select(d => d.Key).ToList();
        Check("Check now is a button row in General > Updates, after the automatic switch",
            rows.IndexOf("general.checkupdates") >= 0 && rows.IndexOf("general.checknow") > rows.IndexOf("general.checkupdates")
            && SettingsCatalog.Build(new AppSettings()).First(d => d.Key == "general.checknow").Kind == SettingKind.Button, string.Join(",", rows));

        var captureDir = Environment.GetEnvironmentVariable("TABFORGE_CAPTURE_DIR");
        foreach (var theme in new[] { "Dark", "Light" })
        {
            var look = new AppSettings();
            ThemeService.ApplyPreset(look.Appearance, theme);
            ThemeService.Apply(look.Appearance);
            Window? received = null;
            var actions = new Views.SettingsWindowActions { CheckForUpdatesNow = w => received = w };
            PreferencesWindow.SetTarget(SettingsCatalog.General, "general.checknow");
            var window = new PreferencesWindow(new AppSettings(), null, null, null, actions);
            try
            {
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(window.Width, window.Height));
                content.Arrange(new Rect(0, 0, window.Width, window.Height));
                content.UpdateLayout();
                var label = (TextBlock)window.FindName("VersionLabel");
                var manage = (FrameworkElement)window.FindName("ManageButton");
                var status = (FrameworkElement)window.FindName("StatusText");
                Check($"{theme}: the Settings version label shows the same line as About ({AppInfo.VersionLine})", label.Text == AppInfo.VersionLine, label.Text);
                var labelBox = label.TransformToAncestor(content).TransformBounds(new Rect(label.RenderSize));
                var manageBox = manage.TransformToAncestor(content).TransformBounds(new Rect(manage.RenderSize));
                var statusBox = status.TransformToAncestor(content).TransformBounds(new Rect(status.RenderSize));
                Check($"{theme}: the version label sits right of Manage settings, is not trimmed and clears the status text",
                    labelBox.Left >= manageBox.Right && labelBox.Right <= statusBox.Left && label.ActualWidth + 0.5 >= MeasureText(label).Width, $"{labelBox} {manageBox} {statusBox} text {MeasureText(label)}");
                Check($"{theme}: the version label has an automation name", System.Windows.Automation.AutomationProperties.GetName(label).Length > 0);

                var button = FindVisual<Button>(content, b => System.Windows.Automation.AutomationProperties.GetName(b) == "Check for updates now");
                Check($"{theme}: the Check now button is built and enabled", button is { IsEnabled: true } && button.Content as string == "Check now");
                if (button is not null)
                {
                    button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Check($"{theme}: Check now hands the Settings window to the update check as the dialog owner", ReferenceEquals(received, window));
                }
                if (captureDir is not null)
                {
                    Directory.CreateDirectory(captureDir);
                    foreach (var zoom in new[] { 1.0, 1.5 })
                        SaveSettingsCapture(content, window, Path.Combine(captureDir, $"settings-version-{theme.ToLowerInvariant()}-{zoom:0.0}x.png"), zoom);
                }
            }
            finally { window.Close(); }
        }
        ThemeService.Apply(new AppSettings().Appearance);
        PreferencesWindow.SetTarget(SettingsCatalog.Home);

        static Size MeasureText(TextBlock t)
        {
            var ft = new System.Windows.Media.FormattedText(t.Text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                new System.Windows.Media.Typeface(t.FontFamily, t.FontStyle, t.FontWeight, t.FontStretch), t.FontSize, System.Windows.Media.Brushes.Black,
                System.Windows.Media.VisualTreeHelper.GetDpi(t).PixelsPerDip);
            return new Size(ft.WidthIncludingTrailingWhitespace, ft.Height);
        }
        static T? FindVisual<T>(DependencyObject root, Func<T, bool> match) where T : DependencyObject
        {
            for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
                if (child is T t && match(t)) return t;
                if (FindVisual(child, match) is { } found) return found;
            }
            return null;
        }
    }

    /// <summary>Renders the laid-out Settings window off screen at a zoom (the DPI a 150% display would use); no screen capture.</summary>
    private static void SaveSettingsCapture(FrameworkElement content, Window window, string path, double zoom)
    {
        var dpi = 96 * zoom;
        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth * zoom), (int)Math.Ceiling(content.ActualHeight * zoom), dpi, dpi, System.Windows.Media.PixelFormats.Pbgra32);
        var back = new System.Windows.Media.DrawingVisual();
        using (var dc = back.RenderOpen()) dc.DrawRectangle(window.Background ?? System.Windows.Media.Brushes.White, null, new Rect(0, 0, content.ActualWidth, content.ActualHeight));
        bitmap.Render(back);
        bitmap.Render(content);
        var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
        encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}
