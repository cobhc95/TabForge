using TabForge.Services.Features;

namespace TabForge.Services.Band;

/// <summary>What the main window offers the Band commands: runs one of them by its catalogue id.</summary>
public interface IBandCommandHost : IFeatureHost
{
    void RunBandCommand(string id);
}

// Owns: everything Band adds to the central tables: the eight "Band view" hotkey rows (each after the one before, from View.BandView),
//   their commands, the "Band view" Preferences rows and their group on the Timeline page.
// Does not own: the Band view (Views/Band), its key handling (BandViewController.Run, reached through the window's RunBandCommand),
//   the saved values (TimelineSettings.Band) or the settings bounds (SettingsValidator).
// Tests: TestFeatureModuleContributions, TestPreferencesCatalog, TestEveryHotkeyIdHasHandler, TestBandSettings.
public sealed class BandFeatureModule : IFeatureModule
{
    public string Name => "Band";

    // Each row follows the one before it: rows that shared one anchor would be inserted in reverse order.
    public IReadOnlyList<FeatureHotkey> Hotkeys { get; } = new FeatureHotkey[]
    {
        new(new("Band.ToggleTrackRow", HotkeyCatalog.CategoryView, "Band view: show / hide the selected track's row", "", "Add the selected track's row to the Band view, or take it out (the same as clicking its pill at the top of the Band view)."), "View.BandView"),
        new(new("Band.RowsMore", HotkeyCatalog.CategoryView, "Band view: more rows per screen", "", "Show one more Band view row on the screen at once (1 to 5, 3 by default); further rows scroll."), "Band.ToggleTrackRow"),
        new(new("Band.RowsFewer", HotkeyCatalog.CategoryView, "Band view: fewer rows per screen", "", "Show one fewer Band view row on the screen at once (1 to 5, 3 by default), so each row is taller."), "Band.RowsMore"),
        new(new("Band.CycleLaneContent", HotkeyCatalog.CategoryView, "Band view: lane content (Tab / Notation / Both)", "", "Switch what the Band view lanes show: the tab, the notation, or both."), "Band.RowsFewer"),
        new(new("Band.CycleLaneLayout", HotkeyCatalog.CategoryView, "Band view: lane layout (Vertical / Horizontal)", "", "Switch the Band view lanes between lines stacked down the lane (vertical) and one line that slides sideways (horizontal)."), "Band.CycleLaneContent"),
        new(new("Band.CycleInstrumentSize", HotkeyCatalog.CategoryView, "Band view: instrument size", "", "Switch the Band view instruments between the full neck (or keyboard), the first 12 frets, and a small keyboard."), "Band.CycleLaneLayout"),
        new(new("Band.ToggleSmoothFollow", HotkeyCatalog.CategoryView, "Band view: smooth / page follow", "", "Switch the Band view lanes between following like the score (its Follow settings) and the Band view own smooth or page-by-page follow."), "Band.CycleInstrumentSize"),
        new(new("Band.ResetRowHeights", HotkeyCatalog.CategoryView, "Band view: reset row heights", "", "Give every Band view row the shared height again (undoes rows resized one by one)."), "Band.ToggleSmoothFollow"),
    };

    public IReadOnlyList<FeatureLayout> Layout { get; } = new FeatureLayout[]
    {
        new(SettingsCatalog.Timeline, "Band view", "band.instrumentsize band.lanecontent band.followscore* band.lanelayout* band.lanezoom* band.playheadline* band.smoothfollow* band.rowsperscreen* band.syncorder*", "Timeline display"),
    };

    public IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => SettingsCatalog.BandRows(settings);

    /// <summary>Every Band hotkey row is a command: the window runs it through the Band view's controller.</summary>
    public IEnumerable<FeatureCommand> Commands(IFeatureHost host)
    {
        var band = host as IBandCommandHost ?? throw new InvalidOperationException("The host does not implement IBandCommandHost.");
        foreach (var row in Hotkeys)
            yield return new(row.Action.Id, () => band.RunBandCommand(row.Action.Id));
    }
}
