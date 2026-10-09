using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, right-click menus built from MenuSpec trees (timeline, fretboard, score): turns the data into WPF menus.
public partial class MainWindow
{
    /// <summary>Saves the timeline's four appearance toggles (they are Preferences rows now) after the View menu or Preferences changed them.</summary>
    private void SaveTimelineAppearance()
    {
        var t = _settings.Timeline;
        t.ShowIndividualNotes = Arrangement.ShowIndividualNotes;
        t.ShowContinuousLine = Arrangement.ShowContinuousBlocks;
        t.HideEmptyGrid = Arrangement.HideEmptyTimelineGrid;
        t.BarGlow = Arrangement.ShowBarGlow;
        t.ShowTrackLines = Arrangement.ShowTrackLines;
        SaveSettings();
    }

    private ContextMenu NewTimelineMenu(string name, IEnumerable<MenuSpec> specs, Action<TimelineCommand> run) =>
        SpecMenus.New(name, specs, spec => run(spec.Command), Arrangement, PlacementMode.MousePoint);

    void Views.Band.IBandViewHost.RunScoreMenu(MenuSpec spec)
    {
        switch (spec.Id)
        {
            case ScoreMenus.ZoomInId: ScoreZoom.ZoomBy(1); break;
            case ScoreMenus.ZoomOutId: ScoreZoom.ZoomBy(-1); break;
            case ScoreMenus.FitWidthId: ScoreZoom.ApplyZoomText("Fit width"); break;
            case BandMenus.SettingsId: OpenSettings(SettingsCatalog.Timeline, BandMenus.SettingsRow); break;
        }
    }
}
