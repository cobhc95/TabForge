using System.Windows;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views;

namespace TabForge;

// MainWindow: the plain menu and key commands, one registry line each.
// Owns: the id -> handler table for commands that only forward to one handler (File, Edit, Bar, Section, Track, Mixer, View, Help...).
// Does not own: the handlers themselves (the partials of the feature), the routers that run first (note commands, panes, effect editors),
// and the commands with conditions or several steps (the RunHotkey switch and RunPaneHotkey in MainWindow.Settings.cs).
// Tests: TestCommandRegistryRouting, TestEveryHotkeyIdHasHandler.
public partial class MainWindow
{
    private CommandRegistry? _commandTable;

    /// <summary>The plain command table; RunHotkey asks it last, after the routers and the switch.</summary>
    private CommandRegistry Commands => _commandTable ??= BuildCommands();

    private CommandRegistry BuildCommands()
    {
        var table = new CommandRegistry();
        void Click(string id, RoutedEventHandler handler) => table.Add(id, () => handler(this, new RoutedEventArgs()));
        void Run(string id, Action run) => table.Add(id, run);

        Click("File.New", New_Click);
        Click("File.NewFromTemplate", ApplyTemplate_Click);
        Click("File.Open", Open_Click);
        Click("File.OpenInNewTab", OpenInNewTab_Click);
        Click("File.Save", Save_Click);
        Click("File.SaveAs", SaveAs_Click);
        Click("File.Print", Print_Click);
        Click("File.Render", Render_Click);
        Click("File.CancelImport", CancelImport_Click);
        Click("File.ProjectSettings", ProjectSettings_Click);
        Click("Transport.Metronome", Metronome_Click);
        Click("Transport.CountIn", CountIn_Click);
        Click("App.CommandPalette", CommandPalette_Click);
        Click("File.PrintPreview", PrintPreview_Click);
        Click("Tab.New", NewTab_Click);
        Click("Tab.Close", CloseTab_Click);
        Click("App.Preferences", Prefs_Click);
        Click("App.Shortcuts", Shortcuts_Click);
        Click("Edit.Undo", Undo_Click);
        Click("Edit.Redo", Redo_Click);
        Click("Edit.Copy", Copy_Click);
        Click("Edit.Cut", Cut_Click);
        Click("Edit.Paste", Paste_Click);
        Click("Edit.PasteSpecial", PasteSpecial_Click);
        Click("Edit.RepeatSelection", RepeatSelection_Click);
        Click("Transport.PlayFromStart", PlayFromStart_Click);
        Click("Transport.Stop", Stop_Click);
        Click("Transport.Loop", Loop_Click);
        Click("Bar.Insert", InsertBar_Click);
        Click("Bar.Delete", DeleteBar_Click);
        Click("Bar.TimeSignature", TimeSig_Click);
        Click("Bar.KeySignature", KeySig_Click);
        Click("Bar.Clef", Clef_Click);
        Click("Bar.Directions", Directions_Click);
        Click("Bar.GoTo", GoTo_Click);
        Click("Bar.First", FirstBar_Click);
        Click("Bar.Last", LastBar_Click);
        Click("Bar.Check", CheckBars_Click);
        Click("Bar.ScoreInfo", ScoreInfo_Click);
        Click("Tools.Transpose", Transpose_Click);
        Click("Tools.Tuner", Tuner_Click);
        Click("Help.Tutorial", Tutorial_Click);
        Click("Help.TutorialDetailed", TutorialDetailed_Click);
        Click("Section.Edit", Section_Click);
        Click("Section.Previous", PrevSection_Click);
        Click("Section.Next", NextSection_Click);
        Click("Note.Chord", Chord_Click);
        Click("Note.Text", Text_Click);
        Click("Track.Add", AddGuitar_Click);
        Click("Track.Delete", DeleteTrack_Click);
        Click("Track.Properties", TrackProps_Click);
        Click("View.Multitrack", Multitrack_Click);
        Click("View.Global", GlobalView_Click);
        Click("Tools.ChordFinder", ChordFinder_Click);
        Click("Tools.SongStats", SongStats_Click);

        Run("Tab.Duplicate", () => DuplicateDocument(_documents.ActiveIndex));
        Run("Edit.SelectAll", () => Editor.SelectAll());
        Run("Transport.PlayPause", TogglePlayback);
        Run("Beat.MixTable", ShowMixTable);
        Run("Section.Add", () => AddSectionAt(Editor.SelectedMeasure));
        Run("View.ToggleAddTrackLane", ToggleAddTrackLane);
        Run("View.Mixer", OpenMixer);
        Run("Transport.Record", ToggleRecording);
        FeatureRegistry.AddCommands(table, this);
        Run("Track.FxChain", () => OpenFxChain(SelectedTrack));
        Run("View.AutoFitTrackList", ToggleAutoFitTrackList);
        Run("View.ResetTrackRowHeight", () => _trackListFit?.ResetRowHeight());
        Run("Track.Wiring", () => OpenWiring(SelectedTrack));
        Run("Playback.SpeedUp", () => ApplySpeed(NextSpeedPreset(_transport.Speed, 1)));
        Run("Playback.SpeedDown", () => ApplySpeed(NextSpeedPreset(_transport.Speed, -1)));
        Run("Playback.SpeedReset", () => ApplySpeed(1.0));
        Run("Track.MoveUp", () => MoveTrack(-1));
        Run("Track.MoveDown", () => MoveTrack(1));
        Run("View.ShowTrackGroups", () => MixerHost.SetTrackListShows("groups", !_project.Mixer.ShowGroupsInTrackList));
        Run("Media.ManageApprovals", () => ReviewLinkedAudio(this));
        Run("Mixer.MasterFx", () => OpenBusFx(null));
        Run("Mixer.CollapseAllGroups", () => SetAllGroupsCollapsed(true));
        Run("Mixer.ExpandAllGroups", () => SetAllGroupsCollapsed(false));
        Run("Mixer.GroupRules", OpenGroupRules);
        Run("Mixer.MonitorFx", OpenMonitorFx);
        Run("Track.MidiProcessing", () => OpenMidiProcessing(SelectedTrack));
        Run("Help.CheckForUpdates", () => _ = CheckForUpdatesAsync(manual: true));
        Run("Track.Next", () => SelectTrack(1));
        Run("Track.Previous", () => SelectTrack(-1));
        Run("View.SmoothFollow", () => SetSmoothFollow(!_follow.Continuous));
        Run("View.HorizontalScroll", () => SetHorizontalScoreView(!Editor.HorizontalScroll));
        Run("View.PlayingBar", () => SetPlayingBar(!_settings.Follow.PlayingBarEnabled));
        Run("View.InstrumentView", () => InstrumentPane.CycleInstrumentView());
        Run("Tools.ScaleFinder", () => InstrumentPane.OpenScaleFinder());
        Run("View.ClearScale", () => InstrumentPane.ClearScaleHighlight());
        Run("View.SidePanel", () => WorkspaceLayouts.ToggleSidePanel());
        Run("View.InstrumentPanel", () => ToggleInstrumentPanel());
        Run("View.BandView", () => WorkspaceLayouts.ToggleBandView());
        Run("View.FretboardPosition", () => WorkspaceLayouts.ToggleInstrumentPosition());
        Run("View.LockInstrumentSize", () => WorkspaceLayouts.ToggleInstrumentSizeLock());
        Run("View.LayoutCompose", () => WorkspaceLayouts.SwitchLayout("Compose"));
        Run("View.LayoutPractice", () => WorkspaceLayouts.SwitchLayout("Practice"));
        Run("View.LayoutMix", () => WorkspaceLayouts.SwitchLayout("Mix"));
        Run("View.Fullscreen", () => WorkspaceLayouts.ToggleFullscreen());
        Run("View.ZoomIn", () => ScoreZoom.ZoomBy(1));
        Run("View.ZoomOut", () => ScoreZoom.ZoomBy(-1));
        return table;
    }
}
