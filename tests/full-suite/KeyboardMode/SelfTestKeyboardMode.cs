using System.IO;
using System.Text.Json;
using TabForge.Docking;
using TabForge.KeyboardMode;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the Keyboard mode layout checks: the layout swap as pure data (sizes, sanitising saved layouts) and a real window going in and out of the mode
//   (the arrangement comes back exactly, nothing swapped is saved, closing the window leaves the normal layout, the toolbar button follows the mode, every track kind shows the keyboard view).
// Does not own: the drawing or the note stream (TestKeyboardModeKeyView, TestKeyboardModeNoteStream), the dock pane row (TestDockPaneTable) or the toolbar fit (TestToolbarZoomSpeedNarrow).
// Tests: TestKeyboardModeLayout.
public static partial class SelfTest
{
    private static string KeyboardModeJson(DockWorkspaceState state) => JsonSerializer.Serialize(state);
    private static HashSet<string> KeyboardModePanels(DockWorkspaceState state) => DockWorkspace.PanelsOf(state).ToHashSet();
    private static DockWorkspaceState KeyboardModeClone(DockWorkspaceState s) => JsonSerializer.Deserialize<DockWorkspaceState>(JsonSerializer.Serialize(s))!;

    private static void TestKeyboardModeLayout()
    {
        KeyboardModeLayoutPureChecks();
        RunInWindowFixture((w, context) =>
        {
            w.Width = 1400; w.Height = 900;
            BandDockSettle(w);
            var dock = VisualDescendants<DockWorkspace>(w).First();
            var settings = context.Store.Settings;
            var mode = w.KeyboardMode;
            var button = w.KeyboardModeButton;
            var track = w.Editor.Project!.Tracks[0];
            w.TrackMixerGrid.SelectedIndex = 0;
            var start = dock.CaptureLayout();

            void Reset()
            {
                if (mode.IsOn) mode.Exit();
                track.Kind = TrackKind.Guitar;
                dock.ApplyLayout(KeyboardModeClone(start));
                BandDockSettle(w);
            }
            void Round(string name, Action prepare, bool instrumentWasOpen)
            {
                Reset();
                prepare();
                BandDockSettle(w);
                var before = KeyboardModeJson(dock.CaptureLayout());
                mode.Enter();
                BandDockSettle(w);
                var during = dock.CaptureLayout();
                Check($"keyboard mode [{name}]: entering shows the Keyboard mode pane and the toolbar button is lit", dock.IsPanelVisible("learn") && mode.IsOn && Equals(button.Tag, "on"), BandDockLayout(dock));
                Check($"keyboard mode [{name}]: the pane replaces the fretboard and the score is hidden", !KeyboardModePanels(during).Contains("instrument") && !HasEditorNode(during.Root));
                var saved = settings.Workspace;
                Check($"keyboard mode [{name}]: the saved settings hold the arrangement it replaced, never the Keyboard mode pane",
                    saved is not null && KeyboardModeJson(saved) == before && !KeyboardModePanels(saved).Contains("learn") && KeyboardModePanels(saved).Contains("instrument") == instrumentWasOpen && settings.Appearance.ShowFretboard == instrumentWasOpen,
                    saved is null ? "no workspace saved" : string.Join(",", KeyboardModePanels(saved)));
                var file = Path.Combine(Path.GetTempPath(), "tf-learn-layout-" + Guid.NewGuid().ToString("N")[..8] + ".json");
                try { SettingsFileService.SaveAtomic(file, settings); Check($"keyboard mode [{name}]: the settings file holds no Keyboard mode pane", SettingsFileService.Load(file).Workspace is { } fw && !KeyboardModePanels(fw).Contains("learn")); }
                finally { try { File.Delete(file); } catch (IOException) { } }
                mode.Exit();
                BandDockSettle(w);
                Check($"keyboard mode [{name}]: leaving brings back the exact arrangement", KeyboardModeJson(dock.CaptureLayout()) == before && !mode.IsOn && button.Tag is null, BandDockLayout(dock));
            }

            Round("default", () => { }, true);
            Round("fretboard closed", () => dock.SetPanelVisible("instrument", false), false);
            Round("band mode on", () => BandDockCommand(w, "View.BandView"), false);
            Round("sizes changed", () => { var s = dock.CaptureLayout(); s.Root!.Ratio = 0.62; if (s.Root.First is { Kind: "split" } f) f.Ratio = 0.55; dock.ApplyLayout(s); }, true);

            // Again after leaving, and a keyboard track.
            Reset();
            mode.Enter(); mode.Exit(); mode.Enter();
            BandDockSettle(w);
            Check("keyboard mode: turning it on again after leaving works", mode.IsOn && dock.IsPanelVisible("learn"));
            mode.Exit();

            // Every kind of track shows the keyboard view; the tracks need no detection, and nothing but the selection matters.
            foreach (var kind in new[] { TrackKind.Keys, TrackKind.Guitar, TrackKind.Bass, TrackKind.Drums })
            {
                Reset();
                track.Kind = kind;
                mode.Enter();
                BandDockSettle(w);
                var shown = dock.CaptureLayout();
                Check($"keyboard mode: a {kind} track hides the score and shows the pane with its own key strip (no keyboard pane)", !HasEditorNode(shown.Root) && KeyboardModePanels(shown).Contains("learn") && !KeyboardModePanels(shown).Contains("instrument") && w.KeyboardModePane.Keys.Element.Visibility == System.Windows.Visibility.Visible, BandDockLayout(dock));
                Check($"keyboard mode: a {kind} track's header names the keyboard view", w.KeyboardModePane.HeaderText.StartsWith("Keyboard view"), w.KeyboardModePane.HeaderText);
                Check($"keyboard mode: with the score hidden the saved layout still has it ({kind})", settings.Workspace is { } kw && HasEditorNode(kw.Root) && !KeyboardModePanels(kw).Contains("learn"));
            }
            Reset();

            // Closing the window while the mode is on leaves the normal arrangement.
            Reset();
            var beforeClose = KeyboardModeJson(dock.CaptureLayout());
            mode.Enter(); BandDockSettle(w);
            typeof(MainWindow).GetMethod("ReleaseWindowResources", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.Invoke(w, null);
            Check("keyboard mode: closing the window while it is on puts the arrangement back", !mode.IsOn && KeyboardModeJson(dock.CaptureLayout()) == beforeClose && KeyboardModeJson(settings.Workspace!) == beforeClose);
        });
    }

    private static void KeyboardModeLayoutPureChecks()
    {
        var baseState = DockLayoutTree.CreateDefaultState();
        baseState.ClosedPanels = DockPaneTable.Ids.Where(id => !KeyboardModePanels(baseState).Contains(id)).ToList();
        var baseJson = KeyboardModeJson(baseState);
        var full = KeyboardModeLayoutSwap.Build(baseState, KeyboardModeSize.Full);
        Check("keyboard layout: Full shows the pane instead of the score, with its own key strip: the keyboard pane is closed", !HasEditorNode(full.Root) && KeyboardModePanels(full).Contains("learn") && !KeyboardModePanels(full).Contains("instrument") && DockLayoutTree.FindPanelHost(full.Root, "instrument") is null && full.ClosedPanels.Contains("instrument") && !full.ClosedPanels.Contains("learn") && KeyboardModeJson(baseState) == baseJson);
        var normal = KeyboardModeLayoutSwap.Build(baseState, KeyboardModeSize.Normal);
        var large = KeyboardModeLayoutSwap.Build(baseState, KeyboardModeSize.Large);
        Check("keyboard layout: Normal and Large keep the score above the pane and differ in size", HasEditorNode(normal.Root) && HasEditorNode(large.Root) && KeyboardModeJson(normal) != KeyboardModeJson(large));
        var registered = DockPaneTable.Ids.ToHashSet();
        Check("keyboard layout: the layouts are valid dock layouts", new[] { normal, large, full }.All(s => DockLayoutTree.Validate(s, true, registered.Contains)));

        // A saved layout that holds the pane loses it; a layout that is only the pane is not a layout.
        var saved = KeyboardModeClone(normal);
        Check("learn layout: a saved layout holding the pane is accepted and the pane is dropped", SettingsFileService.NormalizeWorkspace(saved) && !KeyboardModePanels(saved).Contains("learn"));
        Check("learn layout: a saved layout with no score once the pane is gone is refused (the default layout is used)", !SettingsFileService.NormalizeWorkspace(KeyboardModeClone(full)));
        var settings = new AppSettings { Workspace = KeyboardModeClone(full) };
        Check("learn layout: settings validation drops a saved full-size Keyboard mode layout", SettingsValidator.Normalize(settings).Workspace is null);
    }
}
