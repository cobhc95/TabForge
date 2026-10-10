using System.Reflection;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the generic entry-point parity check. One row per setting that can be changed from Preferences and from another place (context
//   menu, main menu, toolbar, hotkey): the test changes it each way on a real window with two tabs and asserts the same observable state.
// Does not own: the settings rows, the appliers (MainWindow.SettingsApply.cs) or the other entry points.
// Tests: TestSettingsEntryPointParity. To add a setting: add one ParityCase to ParityCases(); docs/SETTINGS_PARITY.md lists the table.
public static partial class SelfTest
{
    /// <param name="Key">The Preferences row key.</param>
    /// <param name="Values">The row values to try (each differs from the one before it).</param>
    /// <param name="OtherEntry">Changes the setting the way the other entry point does (a private window method).</param>
    /// <param name="Observe">The state that must match: view flags of every tab, the stored value, the checked state of the menu.</param>
    private sealed record ParityCase(string Key, string[] Values, Action<MainWindow, string> OtherEntry, Func<MainWindow, string> Observe);

    private static T WinGet<T>(MainWindow w, string name) =>
        (T)(typeof(MainWindow).GetProperty(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(w)
            ?? typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(w))!;

    private static void WinCall(MainWindow w, string name, params object[] args) =>
        typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.Invoke(w, args);

    private static string RowValue(MainWindow w, string key) =>
        Convert.ToString(SettingsCatalog.Build(WinGet<AppSettings>(w, "_settings")).First(d => d.Key == key).Get(), System.Globalization.CultureInfo.InvariantCulture) ?? "";

    /// <summary>The score's per-tab flag for every open tab: the shown tab reads the editor, the others their stored session.</summary>
    private static string PerTab(MainWindow w, Func<TabEditorControl, object> editor, Func<DocumentSession, object> session)
    {
        var shown = WinGet<DocumentSession>(w, "Doc");
        return string.Join("/", w.OpenDocuments.Select(d => ReferenceEquals(d, shown) ? editor(WinGet<TabEditorControl>(w, "Editor")) : session(d)));
    }

    private static IEnumerable<ParityCase> ParityCases()
    {
        yield return new("score.pagelayout", new[] { "Page", "Continuous" },
            (w, v) => WinCall(w, "SetContinuousScoreView", v == "Continuous"),
            w => PerTab(w, e => e.Appearance.CenterSystems, d => d.ContinuousScoreView) + "|" + RowValue(w, "score.pagelayout"));
        yield return new("score.scrolling", new[] { "Horizontal", "Vertical" },
            (w, v) => WinCall(w, "SetHorizontalScoreView", v == "Horizontal"),
            w => PerTab(w, e => e.HorizontalScroll, d => d.HorizontalScoreView) + "|" + RowValue(w, "score.scrolling"));
        yield return new("score.defaultnotation", new[] { "TabOnly", "StaffOnly", "TabAndStaff" },
            (w, v) => WinCall(w, "SetNotation", Enum.Parse<NotationMode>(v)),
            w => PerTab(w, e => e.Notation, d => d.Notation) + "|" + RowValue(w, "score.defaultnotation"));
        yield return new("appearance.paper", new[] { "Light", "Dark" },
            (w, v) => WinCall(w, "SetPaper", v == "Dark"),
            w => WinGet<TabEditorControl>(w, "Editor").Appearance.DarkPaper + "|" + RowValue(w, "appearance.paper"));
        yield return new("score.ledger", new[] { "Standard", "Hidden", "Minimal" },
            (w, v) => WinCall(w, "SetLedgerLines", Enum.Parse<LedgerLineMode>(v)),
            w => WinGet<TabEditorControl>(w, "Editor").Appearance.LedgerLines + "|" + RowValue(w, "score.ledger"));
        yield return new("follow.playingbar", new[] { "True", "False" },
            (w, v) => WinCall(w, "SetPlayingBar", v == "True"),
            w => WinGet<TabEditorControl>(w, "Editor").Appearance.PlayingBarEnabled + "|" + WinGet<System.Windows.Controls.MenuItem>(w, "PlayingBarMenu").IsChecked + "|" + RowValue(w, "follow.playingbar"));
    }

    /// <summary>Preferences and the other entry point leave a window in the same state, with two tabs open.</summary>
    private static void TestSettingsEntryPointParity()
    {
        WithMainWindow(window =>
        {
            WinCall(window, "NewTab");   // a second tab: a setting must reach tabs that are not shown
            var probes = new Diagnostics.WindowProbes(window);
            foreach (var c in ParityCases())
            {
                foreach (var value in c.Values)
                {
                    var other = c.Values.First(v => v != value);
                    c.OtherEntry(window, other);
                    var copy = SettingsMigration.Clone(WinGet<AppSettings>(window, "_settings"));
                    var row = SettingsCatalog.Build(copy).First(d => d.Key == c.Key);
                    row.Set(row.Kind == SettingKind.Bool ? bool.Parse(value) : value);
                    probes.PreviewPreferences(copy);
                    var viaPreferences = c.Observe(window);

                    c.OtherEntry(window, other);
                    c.OtherEntry(window, value);
                    var viaOther = c.Observe(window);
                    Check($"settings parity: {c.Key} = {value} is the same from Preferences and from its other entry point", viaPreferences == viaOther,
                        $"Preferences '{viaPreferences}', other '{viaOther}'");
                }
            }
        });
    }
}
