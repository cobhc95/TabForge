using System.Reflection;
using System.Windows.Controls;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the real-window checks of the settings that have more than one way in (View and transport menu items, Settings rows) and of a second window following a Settings change.
// Does not own: the catalogue-wide audits (SelfTestSettingsWiring.cs).
/// <summary>A setting changed in the Settings window shows on every menu item and button that carries it; the menu item writes the same setting back; another window follows.</summary>
public static partial class SelfTest
{
    private static void TestSettingsEntryPointsInSync() => RunInWindowFixture((a, context) =>
    {
        var store = context.Store;
        var failures = new List<string>();
        void Is(string what, bool ok) { if (!ok) failures.Add(what); }
        // What the Settings window does while it is open: stage a copy, change it, preview it (the window swaps the shared object).
        void Preview(MainWindow window, Action<AppSettings> change)
        {
            var staged = SettingsMigration.Clone(store.Settings);
            change(staged);
            LtCall(window, "PreviewPreferences", staged);
        }
        // As the menu does: a checkable item flips its mark, then raises Click.
        void Click(MainWindow window, string menuName)
        {
            var item = LtField<MenuItem>(window, menuName)!;
            if (item.IsCheckable) item.IsChecked = !item.IsChecked;
            item.RaiseEvent(new System.Windows.RoutedEventArgs(MenuItem.ClickEvent));
        }
        bool Checked(MainWindow window, string menuName) => LtField<MenuItem>(window, menuName)!.IsChecked;

        // Metronome and count-in: the row checks the menu item, the menu item writes the row's value.
        Preview(a, s => s.Audio.Metronome = true);
        Is("metronome row -> menu item", Checked(a, "MetronomeMenu"));
        Click(a, "MetronomeMenu");
        Is("metronome menu item -> row", !store.Settings.Audio.Metronome && !Checked(a, "MetronomeMenu"));
        Click(a, "CountInMenu");
        Is("count-in menu item -> row (saved at once)", store.Settings.Audio.CountIn && Checked(a, "CountInMenu"));
        Preview(a, s => s.Audio.CountIn = false);
        Is("count-in row -> menu item", !Checked(a, "CountInMenu"));

        // Note preview: the menu item saves at once, and the Settings window starts from it.
        Preview(a, s => s.Audio.PreviewNotes = true);
        Is("preview-notes row -> menu item", Checked(a, "PreviewNotesMenu"));
        Click(a, "PreviewNotesMenu");
        Is("preview-notes menu item -> row (saved at once)", !store.Settings.Audio.PreviewNotes);

        // Fretboard and arrangement panels: row <-> View menu.
        Preview(a, s => s.Appearance.ShowFretboard = false);
        Is("fretboard row -> View menu item", !Checked(a, "InstrumentViewMenu"));
        Click(a, "InstrumentViewMenu");
        Is("fretboard View menu item -> row", store.Settings.Appearance.ShowFretboard);
        Preview(a, s => s.Appearance.ShowArrangementOverview = false);
        Is("arrangement row -> View menu item", !Checked(a, "ArrangementMenu"));

        // Timeline note drawing and the playing bar.
        Preview(a, s => { s.Timeline.ShowContinuousLine = true; s.Timeline.ShowIndividualNotes = false; });
        Is("continuous-line row -> both View menu items", Checked(a, "ArrangementContinuousBlocksMenu") && !Checked(a, "ArrangementIndividualNotesMenu"));
        Click(a, "ArrangementIndividualNotesMenu");
        Is("individual-notes View menu item -> rows", store.Settings.Timeline.ShowIndividualNotes && !store.Settings.Timeline.ShowContinuousLine);
        Preview(a, s => s.Follow.PlayingBarEnabled = !s.Follow.PlayingBarEnabled);
        var playingBar = store.Settings.Follow.PlayingBarEnabled;
        Is("playing-bar row -> View menu item", Checked(a, "PlayingBarMenu") == playingBar);
        Click(a, "PlayingBarMenu");
        Is("playing-bar View menu item -> row", store.Settings.Follow.PlayingBarEnabled == !playingBar && Checked(a, "PlayingBarMenu") == !playingBar);
        Check("settings entry points: every menu item and its Settings row stay in step, both ways", failures.Count == 0, string.Join("; ", failures));

        // A second window follows a Settings change made in the first.
        var b = NewLifetimeWindow();
        try
        {
            var arrangementB = LtField<ArrangementPanel>(b, "Arrangement")!;
            var linesBefore = arrangementB.ShowTrackLines;
            var staged = SettingsMigration.Clone(store.Settings);
            staged.Timeline.ShowTrackLines = !linesBefore;
            staged.Audio.Metronome = true;
            store.Replace(staged, a);   // what window A's Settings window does on every preview
            SettleLifetimeDispatcher();
            Check("settings: a second window shows what the first window's Settings window changed", arrangementB.ShowTrackLines == !linesBefore && Checked(b, "MetronomeMenu"),
                $"track lines {arrangementB.ShowTrackLines} (was {linesBefore}), metronome item {Checked(b, "MetronomeMenu")}");
        }
        finally { b.Close(); }
    });

    /// <summary>Every row, previewed in a real window, is applied without an error and is still the stored value after the window copies its own state into the settings (a window state that is not driven by the row would overwrite it).</summary>
    private static void TestSettingsEveryRowAppliesInWindow() => RunInWindowFixture((a, context) =>
    {
        var store = context.Store;
        var baseline = SettingsMigration.Clone(store.Settings);
        var threw = new List<string>();
        var overwritten = new List<string>();
        var keys = SettingsCatalog.Build(baseline).Where(d => d.Kind != SettingKind.Button && !WiringMachineRow(d.Key) && !SettingsWithSideEffects.Contains(d.Key)).Select(d => d.Key).ToList();
        foreach (var key in keys)
        {
            var staged = SettingsMigration.Clone(baseline);
            var row = SettingsCatalog.Build(staged).First(d => d.Key == key);
            if (AlternativeValue(row) is not { } value) continue;
            row.Set(value);
            var want = row.Get();
            try
            {
                LtCall(a, "PreviewPreferences", staged);
                LtCall(a, "CaptureWindowState");
            }
            catch (Exception ex) { threw.Add($"{key} ({ex.GetType().Name}: {ex.Message})"); continue; } // Not logged: the failure is the check result
            var now = SettingsCatalog.Build(store.Settings).First(d => d.Key == key).Get();
            if (!SameValue(now, want)) overwritten.Add($"{key} (set {Format(want)}, window state gave {Format(now)})");
            LtCall(a, "PreviewPreferences", SettingsMigration.Clone(baseline));
        }
        Check("every settings row can be previewed in a window without an error", threw.Count == 0, string.Join("; ", threw.Take(8)));
        Check("a previewed row is not overwritten when the window copies its own state into the settings", overwritten.Count == 0, string.Join("; ", overwritten));
    });
}
