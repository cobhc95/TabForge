using System.IO;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard show/hide value belongs to the editor mode (Tab, piano roll, Band): a layout saved while the piano roll hid the
/// pane never changes the Tab value, a piano roll choice never changes the Tab value and vice versa, Band mode writes neither, a mode
/// switch writes nothing, the new setting migrates and round-trips, and the Settings rows edit their own value.
/// Does not own: the dock tree (DockWorkspaceRules), the settings file format (SettingsFileService), the gate's UI wiring (InstrumentPanelRollHoldTests).
/// Tests: FretboardVisibilityMemory, MainWindowSettings, SettingsCatalog.Fretboard, FretboardStartupLayout.
/// </summary>
public sealed class FretboardVisibilityMemoryTests : System.IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "fretboard-mode-" + System.Guid.NewGuid().ToString("N"));

    public FretboardVisibilityMemoryTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static DockWorkspaceState LayoutWithoutFretboard()
    {
        var state = DockWorkspaceController.DefaultLayout();
        DockWorkspaceRules.Close(state, "instrument");
        return DockWorkspaceRules.Capture(state);
    }

    private static (DockWorkspaceController Dock, InstrumentPanelGate Gate, MainWindowSettings Settings) Rig(string path)
    {
        var settings = MainWindowSettings.Open(path);
        var setup = new DockPaneSetup();
        foreach (var id in DockPaneTable.Ids) setup.Register(id, () => new Border());
        setup.RegisterEditor(() => new Border());
        var dock = new DockWorkspaceController(new DockWorkspaceView(setup.ContentOf), setup, settings.StartLayout);
        FretboardMode Mode() => FretboardMode.Tab;
        var gate = new InstrumentPanelGate(dock, _ => { }, Mode,
            mode => settings.RememberedFretboard(mode), (mode, shown) => settings.RememberFretboard(mode, shown));
        return (dock, gate, settings);
    }

    [AvaloniaFact]
    public void ALayoutSavedInThePianoRollKeepsTheTabValue()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        Assert.True(settings.Settings.Appearance.ShowFretboard);
        settings.SaveLayout(LayoutWithoutFretboard());   // what the dock captures while the roll hides the pane
        Assert.True(settings.Settings.Appearance.ShowFretboard);
        Assert.False(settings.Settings.Appearance.ShowFretboardInPianoRoll);
    }

    [AvaloniaFact]
    public void AModeSwitchAloneWritesNothing()
    {
        var (_, gate, settings) = Rig(Path.Combine(_folder, "settings.json"));
        settings.Settings.Appearance.ShowFretboard = true;
        settings.Settings.Appearance.ShowFretboardInPianoRoll = false;
        gate.RollChanged(true);
        gate.RollChanged(false);
        Assert.True(settings.Settings.Appearance.ShowFretboard);
        Assert.False(settings.Settings.Appearance.ShowFretboardInPianoRoll);
    }

    [AvaloniaFact]
    public void PianoRollAndTabRememberTheirOwnValues()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        Assert.True(settings.RememberFretboard(FretboardMode.PianoRoll, true));
        Assert.True(settings.Settings.Appearance.ShowFretboardInPianoRoll);
        Assert.True(settings.Settings.Appearance.ShowFretboard);   // untouched
        Assert.True(settings.RememberFretboard(FretboardMode.Tab, false));
        Assert.False(settings.Settings.Appearance.ShowFretboard);
        Assert.True(settings.Settings.Appearance.ShowFretboardInPianoRoll);   // untouched
    }

    [AvaloniaFact]
    public void BandModeWritesNeitherValue()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var beforeTab = settings.Settings.Appearance.ShowFretboard;
        var beforeRoll = settings.Settings.Appearance.ShowFretboardInPianoRoll;
        Assert.False(settings.RememberFretboard(FretboardMode.Band, !beforeTab));
        Assert.Equal(beforeTab, settings.Settings.Appearance.ShowFretboard);
        Assert.Equal(beforeRoll, settings.Settings.Appearance.ShowFretboardInPianoRoll);
        Assert.Null(settings.RememberedFretboard(FretboardMode.Band));
    }

    [AvaloniaFact]
    public void TheStartLayoutRestoresTheTabValueAfterClosingInThePianoRoll()
    {
        var path = Path.Combine(_folder, "settings.json");
        var first = MainWindowSettings.Open(path);
        first.Settings.Workspace = LayoutWithoutFretboard();
        first.Settings.Appearance.ShowFretboard = true;
        first.SaveSettings();
        first.Dispose();

        using var again = MainWindowSettings.Open(path);
        var layout = again.StartLayout;
        Assert.Contains("instrument", DockWorkspaceState.EnumeratePanelIds(layout.Root));
    }

    [AvaloniaFact]
    public void OldSettingsKeepTheTabValueAndDefaultThePianoRollOff()
    {
        var path = Path.Combine(_folder, "old.json");
        File.WriteAllText(path, """{"Appearance":{"ShowFretboard":true}}""");
        var loaded = SettingsFileService.Load(path);
        Assert.True(loaded.Appearance.ShowFretboard);
        Assert.False(loaded.Appearance.ShowFretboardInPianoRoll);

        File.WriteAllText(path, """{"Appearance":{"ShowFretboard":false}}""");
        var hidden = SettingsFileService.Load(path);
        Assert.False(hidden.Appearance.ShowFretboard);
        Assert.False(hidden.Appearance.ShowFretboardInPianoRoll);
    }

    [AvaloniaFact]
    public void BothValuesRoundTripThroughTheSettingsFile()
    {
        var path = Path.Combine(_folder, "roundtrip.json");
        var settings = new AppSettings();
        settings.Appearance.ShowFretboard = false;
        settings.Appearance.ShowFretboardInPianoRoll = true;
        SettingsFileService.SaveAtomic(path, settings);
        var loaded = SettingsFileService.Load(path);
        Assert.False(loaded.Appearance.ShowFretboard);
        Assert.True(loaded.Appearance.ShowFretboardInPianoRoll);
    }

    [AvaloniaFact]
    public void TheSettingsRowsEditTheirOwnMode()
    {
        var settings = new AppSettings();
        var rows = SettingsCatalog.Build(settings).ToList();
        var tab = rows.Single(row => row.Key == "appearance.fretboard");
        var roll = rows.Single(row => row.Key == "appearance.fretboard.pianoroll");
        Assert.Equal("Show the fretboard (Tab mode)", tab.Title);
        Assert.Equal("Show the fretboard in the piano roll", roll.Title);
        tab.Set(false);
        roll.Set(true);
        Assert.False(settings.Appearance.ShowFretboard);
        Assert.True(settings.Appearance.ShowFretboardInPianoRoll);
    }
}
