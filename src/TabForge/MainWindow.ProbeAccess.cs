using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Audio;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// MainWindow, command-line probes: the probes (TabForge.Diagnostics.WindowProbes) reach the window through ProbeAccess, a nested class, so the members
// they use stay private to everything else. The few probe-related members the window itself uses are here too.
public partial class MainWindow
{
    /// <summary>`--theme Light|Dark|...` for test runs: switches the live theme without saving it.</summary>
    public void ApplyThemeOverride(string mode)
    {
        ThemeService.ApplyPreset(_settings.Appearance, mode);
        ApplyAppearance();
    }

    internal static T? FindVisual<T>(DependencyObject root) where T : DependencyObject => FindVisuals<T>(root).FirstOrDefault();

    internal static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var nested in FindVisuals<T>(child)) yield return nested;
        }
    }

    /// <summary>Set while a capture script runs: right-click menus are handed to it instead of being opened on screen.</summary>
    internal static Action<ContextMenu>? ContextMenuCapture;

    /// <summary>
    /// What the probes may use of the window: one forwarder per member, nothing else (the member count is pinned by the architecture guard).
    /// A probe derives from it, so it reads like code inside the window.
    /// </summary>
    internal class ProbeAccess
    {
        private readonly MainWindow _window;

        protected ProbeAccess(MainWindow window) => _window = window;

        /// <summary>The window itself, for the places a probe needs a window to own a dialog or to measure.</summary>
        internal MainWindow Window => _window;

        internal static Action<ContextMenu>? ContextMenuCapture { get => MainWindow.ContextMenuCapture; set => MainWindow.ContextMenuCapture = value; }

        internal ScoreTimeline? _timeline { get => _window._timeline; set => _window._timeline = value; }

        internal void ApplyThemeOverride(string mode) => _window.ApplyThemeOverride(mode);

        internal static T? FindVisual<T>(DependencyObject root) where T : DependencyObject => MainWindow.FindVisual<T>(root);

        internal static IEnumerable<T> FindVisuals<T>(DependencyObject root) where T : DependencyObject => MainWindow.FindVisuals<T>(root);

        internal System.Windows.Threading.Dispatcher Dispatcher => _window.Dispatcher;
        internal double ActualWidth => _window.ActualWidth;
        internal double ActualHeight => _window.ActualHeight;
        internal void UpdateLayout() => _window.UpdateLayout();
        internal object FindResource(object key) => _window.FindResource(key);
        internal void AddTrackWithWindow() => _window.AddTrackWithWindow();
        internal void AddAudioTrack() => _window.AddAudioTrack();
        internal ContextMenu TrackRowMenu(int index) => _window.TrackRowMenu(index);
        internal void ApplyAppearance() => _window.ApplyAppearance();
        internal void ApplyLoopRange(int start, int end, int startCell = 0, int endCell = -1) => _window.ApplyLoopRange(start, end, startCell, endCell);
        internal void ApplyTemplate_Click(object sender, RoutedEventArgs e) => _window.ApplyTemplate_Click(sender, e);
        internal TabForge.Views.ArrangementPanel Arrangement => _window.Arrangement;
        internal System.Windows.Controls.Grid ArrangementHost => _window.ArrangementHost;
        internal System.Windows.Controls.MenuItem ArrangementMenu => _window.ArrangementMenu;
        internal PlaybackOptions BuildOptions() => _window.BuildOptions();
        internal void ClipsChanged(bool refreshRows = false) => _window._clips.Changed(_window.Doc, refreshRows);
        internal System.Windows.Controls.Border ControllerPanel => _window.ControllerPanel;
        internal System.Windows.Controls.Button CountInButton => _window.CountInButton;
        internal void CountInButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => _window.CountInButton_PreviewMouseRightButtonUp(sender, e);
        internal System.Windows.Controls.Primitives.Popup CountInSettingsPopup => _window.CountInSettingsPopup;
        internal void CountIn_Click(object sender, RoutedEventArgs e) => _window.CountIn_Click(sender, e);
        internal TabForge.Views.TabEditorControl Editor => _window.Editor;
        internal TabForge.Views.InstrumentPanel Instrument => _window.Instrument;
        internal System.Windows.Controls.Border InstrumentHost => _window.InstrumentHost;
        internal System.Windows.Controls.MenuItem InstrumentViewMenu => _window.InstrumentViewMenu;
        internal void Instrument_MouseRightButtonUp(object sender, MouseButtonEventArgs e) => _window.Instrument_MouseRightButtonUp(sender, e);
        internal bool IsRecording => _window.IsRecording;
        internal System.Windows.Controls.Button LoopButton => _window.LoopButton;
        internal void LoopButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => _window.LoopButton_PreviewMouseRightButtonUp(sender, e);
        internal System.Windows.Controls.Primitives.Popup LoopSettingsPopup => _window.LoopSettingsPopup;
        internal System.Windows.Controls.ScrollViewer LowerPanelScroll => _window.LowerPanelScroll;
        internal int MaxMeasures() => _window.MaxMeasures();
        internal System.Windows.Controls.Button MetronomeButton => _window.MetronomeButton;
        internal void MetronomeButton_PreviewMouseRightButtonUp(object sender, MouseButtonEventArgs e) => _window.MetronomeButton_PreviewMouseRightButtonUp(sender, e);
        internal System.Windows.Controls.Primitives.Popup MetronomeSettingsPopup => _window.MetronomeSettingsPopup;
        internal Task OpenDocumentFromPath(string path, bool replaceCurrent = false, bool replaceAll = false) => _window.OpenDocumentFromPath(path, replaceCurrent, replaceAll);
        internal void OpenScaleFinder() => _window.OpenScaleFinder();
        internal void OpenStartupFile(string path, bool background = true) => _window.OpenStartupFile(path, background);
        internal void PlayFromStart() => _window.PlayFromStart();
        internal DocumentPlaybackState Playback => _window.Playback;
        internal TabForge.Views.PlayheadOverlay Playhead => _window.Playhead;
        internal void Prefs_Click(object sender, RoutedEventArgs e) => _window.Prefs_Click(sender, e);
        internal void PreviewPreferences(AppSettings settings) => _window.PreviewPreferences(settings);
        internal void ProjectSettings_Click(object sender, RoutedEventArgs e) => _window.ProjectSettings_Click(sender, e);
        internal RecordingController Recorder => _window.Recorder;
        internal void RefreshArrangement() => _window.RefreshArrangement();
        internal void RefreshInstrument() => _window.RefreshInstrument();
        internal void RefreshLayoutsMenu() => _window.RefreshLayoutsMenu();
        internal void RefreshTracks() => _window.RefreshTracks();
        internal void ScoreInfo_Click(object sender, RoutedEventArgs e) => _window.ScoreInfo_Click(sender, e);
        internal System.Windows.Controls.ScrollViewer ScoreScroll => _window.ScoreScroll;
        internal void ScrollToCursor() => _window.ScrollToCursor();
        internal System.Windows.Controls.ScrollViewer SectionsPanelContent => _window.SectionsPanelContent;
        internal TrackModel? SelectedTrack => _window.SelectedTrack;
        internal void SetContinuousScoreView(bool continuous) => _window.SetContinuousScoreView(continuous);
        internal void SetHorizontalScoreView(bool horizontal) => _window.SetHorizontalScoreView(horizontal);
        internal void SetInstrumentView(string? view, TrackModel? track = null) => _window.SetInstrumentView(view, track);
        internal void SetLoopActive(bool loop) => _window.SetLoopActive(loop);
        internal void SetNotation(NotationMode mode) => _window.SetNotation(mode);
        internal void SetPaper(bool dark) => _window.SetPaper(dark);
        internal void ShowArrangementContextMenu(int bar, int trackIndex, bool fromKeyboard = false) => _window.ShowArrangementContextMenu(bar, trackIndex, fromKeyboard);
        internal void ShowGlobalTuningWindow() => _window.ShowGlobalTuningWindow();
        internal void ShowInstrumentContextMenu(bool fromKeyboard) => _window.ShowInstrumentContextMenu(fromKeyboard);
        internal void ShowMixTable() => _window.ShowMixTable();
        internal void ShowNoteContextMenu(Views.ContextMenuEventArgs target) => _window.ShowNoteContextMenu(target);
        internal void ShowScoreContextMenu(Point position, Views.ContextMenuEventArgs? target = null) => _window.ShowScoreContextMenu(position, target);
        internal void ShowScoreTextStyleWindow() => _window.ShowScoreTextStyleWindow();
        internal Audio.SongClock SongClock => _window.SongClock;
        internal System.Windows.Controls.TextBlock StatusText => _window.StatusText;
        internal void StopPlayback() => _window.StopPlayback();
        internal void SwitchLayout(string name) => _window.SwitchLayout(name);
        internal void SyncFromSettings(bool applyWindowSize) => _window.SyncFromSettings(applyWindowSize);
        internal System.Windows.Controls.Border TitleBar => _window.TitleBar;
        internal void ToggleArm(TrackModel track) => _window.ToggleArm(track);
        internal void TogglePlayback() => _window.TogglePlayback();
        internal void ToggleRecording() => _window.ToggleRecording();
        internal System.Windows.Controls.Border ToolsPanelContent => _window.ToolsPanelContent;
        internal System.Windows.Controls.DataGrid TrackMixerGrid => _window.TrackMixerGrid;
        internal void TrackProps_Click(object sender, RoutedEventArgs e) => _window.TrackProps_Click(sender, e);
        internal void Undo_Click(object sender, RoutedEventArgs e) => _window.Undo_Click(sender, e);
        internal bool _confirmOnClose { get => _window._confirmOnClose; set => _window._confirmOnClose = value; }
        internal bool _countIn { get => _window._transport.CountIn; set => _window._transport.CountIn = value; }
        internal DockWorkspace? _dockWorkspace { get => _window._dockWorkspace; set => _window._dockWorkspace = value; }
        internal DocumentManager _documents => _window._documents;
        internal ScoreFollowCoordinator _follow => _window._follow;
        internal MenuItem? _layoutsMenu { get => _window._layoutsMenu; set => _window._layoutsMenu = value; }
        internal PlaybackEngine _midi => _window._midi;
        internal Dictionary<string, FrameworkElement> _palettePanelContents => _window._palettePanelContents;
        internal int _playheadBar { get => _window._playheadBar; set => _window._playheadBar = value; }
        internal double _playheadFraction { get => _window._playheadFraction; set => _window._playheadFraction = value; }
        internal bool _probeNoSave { get => _window._probeNoSave; set => _window._probeNoSave = value; }
        internal SongProject _project { get => _window._project; set => _window._project = value; }
        internal string? _scaleHighlight { get => _window._scaleHighlight; set => _window._scaleHighlight = value; }
        internal SelectionModel _selection => _window._selection;
        internal AppSettings _settings { get => _window._settings; set => _window._settings = value; }
    }
}
