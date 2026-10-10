using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using Microsoft.Win32;
using TabForge.Controllers;
using TabForge.Docking;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Shell;
using TabForge.Views;
using TabForge.Visualization;

namespace TabForge;

// MainWindow, arrangement timeline: refresh, section moves, selected area and context menus.
// Owns: the arrangement timeline's refresh, section moves, selected area and the section lane menu; the other timeline menus are TimelineMenuController (its host is here).
// Does not own: the timeline control (ArrangementPanel) and the song's section model.
// Tests: listed in docs/feature-map/timeline-and-clips.md.
public partial class MainWindow
{
    // ---------- arrangement ----------

    /// <param name="keepRows">The edit changed bars or clips only: track rows whose tracks, heights and header state are unchanged stay as they are.</param>
    private void RefreshArrangement(bool keepRows = false)
    {
        Arrangement.Bind(_project, MidiDevices, Doc.Media, keepRows);
        Arrangement.SetSelectedBar(Editor.SelectedMeasure);
        Arrangement.SetSelectedTrack(Math.Max(0, TrackMixerGrid.SelectedIndex));
        // Every structural edit, undo/redo and track change ends here: the bar count may have changed.
        ReconcileSelection();
        SyncArrangementPlayhead();
        var (ls, le) = GetLoopRange();
        Arrangement.SetLoopRange(ls, le);
        SyncAreaVisuals();   // also the skipped areas: undo, redo and structural edits move them with their bars
    }

    private void RefreshTimelineOverviewGeometry() => Arrangement.RefreshTimelineGeometry();

    private void RefreshArrangementScore() => Arrangement.RefreshScore();

    private void RefreshArrangementSelection()
    {
        Arrangement.SetSelectedBar(Editor.SelectedMeasure);
        Arrangement.SetScoreSelection(_selection.BarRange, _selection.ScopeTrack);
        Arrangement.SetSelectedTrack(Math.Max(0, TrackMixerGrid.SelectedIndex));
        SyncArrangementPlayhead();
    }

    private void SyncArrangementPlayhead()
    {
        var hasPlaybackPosition = _isPlayingVisual && _playheadBar >= 0;
        Arrangement.SetPlayhead(
            hasPlaybackPosition ? _playheadBar : Editor.SelectedMeasure,
            hasPlaybackPosition ? _playheadFraction : 0,
            hasPlaybackPosition,
            hasPlaybackPosition && _midi.IsPaused);
    }

    private void OnArrangementEdited()
    {
        DocumentEdits.MarkChanged(Doc);
        Editor.InvalidateScoreLayout();
        RefreshArrangement();
        RefreshInstrument();
        RefreshTabs();
        UpdateTitle();
        _midi.Rebuild(_project);   // mute/solo/volume/pan must be heard immediately
    }

    private void OnArrangementTrackColorChanged()
    {
        _project.IsDirty = true;
        Arrangement.RefreshScore();
        RefreshTabs();
        UpdateTitle();
    }

    private void OnArrangementMixChanged()
    {
        _project.IsDirty = true;
        _midi.RefreshMix(_project);       // live volume/pan, no timeline rebuild
        MixerHost.Windows.Mixer?.SyncValues();       // an open mixer follows the track list: values in place, rebuild only on structure change
        RefreshTabs();
        UpdateTitle();
    }

    private sealed class ArrangementGestureHost : IArrangementGestureHost
    {
        private readonly MainWindow _window;
        public ArrangementGestureHost(MainWindow window) => _window = window;
        public DocumentSession Document => _window.Doc;
    }

    private void CompleteTrackEditUndo() => _gestures.CompleteTrackEdit();

    /// <summary>
    /// Selects bars start…end in both views; the selection is the loop area (applied by the model's observer).
    /// </summary>
    private void ApplyLoopRange(int start, int end, int startCell = 0, int endCell = -1, SelectionScope? scope = null) =>
        _selection.SetRange(Editor.SelectedTrackIndex, start, end, SelectionOrigin.Command, startCell, endCell, scope);

    /// <summary>Song or track selection changed: keep the mixer, FX windows and audio engine in step.</summary>
    private void RefreshPluginChain() => SyncMixerWindows();

    private void RefreshMarkers()
    {
        MarkerList.ItemsSource = null;
        var sections = _project.Markers.OrderBy(m => m.MeasureIndex).ToList();
        // Resolve with the timeline's rule first, so the list shows exactly the timeline's colours.
        SectionColours.Resolve(sections, Arrangement.MatchSimilarSectionColours, Color.FromRgb(0x2E, 0x74, 0xB5));
        MarkerList.ItemsSource = sections;
        Arrangement.RefreshSections();
        UpdatePlayingSectionMarker(_isPlayingVisual ? _playheadBar : -1, forceRefresh: true);
    }

    private void MoveSection(int from, int insertBefore)
    {
        var selectedBar = Editor.SelectedMeasure;
        var dragStart = _gestures.TakeSectionSnapshot();
        if (_arrangementController.MoveSection(Doc, from, insertBefore, dragStart).Value is not { } mapping) return;   // one undo step, one dirty change, one timeline invalidation
        _selection.Remap(mapping, MaxMeasures());   // the selected range follows its bars
        SyncAreaVisuals();   // the skipped areas moved with them (in the controller)
        if (selectedBar >= 0 && selectedBar < mapping.Length)
            Editor.SetPosition(mapping[selectedBar], Editor.SelectedCell, Editor.SelectedString, seekPlayback: false);
        if (Playback.ApplySectionMove(mapping))   // the document's remap and playhead bar; the views follow below
        {
            Editor.Playback.BarRemap = _playbackBarRemap;
            var engine = Playback.Engine;
            _ = engine.RefreshArrangementInBackground(_project, _playbackBarRemap!);   // copies the song here; the pool thread compiles the copy
        }
        Editor.InvalidateScoreLayout();
        Arrangement.RefreshSectionOrder(Editor.SelectedMeasure, Math.Max(0, TrackMixerGrid.SelectedIndex));
        RefreshMarkers();
        UpdateTitle();
        if (_isPlayingVisual && _playheadBar >= 0)
        {
            Editor.Playback.SetPlayhead(_playheadBar, _playheadCell);
            Playhead.SetGeometry(Editor.Playback.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.Playback.DurationGeometries());
        }
        SyncAudioEngine();   // clips that moved with the section
        StatusText.Text = "Section moved";
    }

    private void UpdatePlayingSectionMarker(int bar, bool forceRefresh = false)
    {
        var current = _isPlayingVisual && bar >= 0
            ? SectionLayout.At(_project, bar)
            : null;
        var changed = _gestures.ShowPlayingSection(current, forceRefresh, marker => MarkerList.SelectedItem = marker);
        if (changed && current is not null) MarkerList.ScrollIntoView(current);
    }



    // ---------- selected area (bar range picked by dragging the timeline or selecting in the score) ----------

    private List<(int Start, int End)> _skipRanges => Doc.SkipRanges;

    /// <summary>
    /// The timeline marks the loop only when it is a real selection (an area, or the section loop). Looping
    /// the whole song needs no highlight over every bar: playback simply returns to bar 1 at the end.
    /// </summary>
    private bool ShowLoopOnTimeline
    {
        get
        {
            if (!_loop) return false;
            if (_selLoop.HasArea) return true;
            var (start, end) = GetLoopRange();
            return start > 0 || end < Math.Max(0, MaxMeasures() - 1);
        }
    }

    private void SyncAreaVisuals()
    {
        Arrangement.SetAreaVisible(_selLoop.HasArea);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        Arrangement.SetSkipRanges(_skipRanges);
        _midi.SetSkipRanges(_skipRanges);
    }

    private TimelineMenuController? _timelineMenus;
    private TimelineMenuController TimelineMenusFlow => _timelineMenus ??= new TimelineMenuController(new TimelineMenuHost(this), _selection, _selLoop, _sections, _arrangementController, _clips);

    /// <summary>The selection menu (Copy / Cut / Paste / Delete on top, the rest in submenus).</summary>
    private ContextMenu BuildSelectionMenu() => TimelineMenusFlow.BuildSelectionMenu();

    private void ShowTimelineContextMenuFromKeyboard() => TimelineMenusFlow.ShowFromKeyboard();

    private void ShowArrangementContextMenu(int bar, int trackIndex, bool fromKeyboard = false) => TimelineMenusFlow.ShowBarMenu(bar, trackIndex, fromKeyboard);

    private void ShowSectionContextMenu(int markerIndex, int? clickedBar = null) => TimelineMenusFlow.ShowSectionMenu(markerIndex, clickedBar);

    private sealed class TimelineMenuHost : ITimelineMenuHost
    {
        private readonly MainWindow _window;
        public TimelineMenuHost(MainWindow window) => _window = window;
        public DocumentSession Document => _window.Doc;
        public AppSettings Settings => _window._settings;
        public bool LoopOn => _window._loop;
        public int SelectedTrackRow => _window.TrackMixerGrid.SelectedIndex;
        public TrackModel? SelectedTrack => _window.SelectedTrack;
        public int KeyboardMenuBar => _window._isPlayingVisual && _window._playheadBar >= 0 ? _window._playheadBar : _window.Editor.SelectedMeasure;
        public string MenuKey(string id) => _window.MenuKey(id);
        public ContextMenu NewMenu(string name, IEnumerable<MenuSpec> specs, Action<TimelineCommand> run) => _window.NewTimelineMenu(name, specs, run);
        public void OpenMenu(ContextMenu menu, Point? anchor, bool fromKeyboard) => SpecMenus.Open(menu, _window.Arrangement, anchor, fromKeyboard);
        public Point? BarAnchor(int bar) => _window.Arrangement.TimelineBarAnchor(bar);
        public void OpenSettings(string category, string row) => _window.OpenSettings(category, row);
        public void SetLoopActive(bool loop) => _window.SetLoopActive(loop);
        public void ApplyLoopRange(int start, int end, SelectionScope scope) => _window.ApplyLoopRange(start, end, scope: scope);
        public void SyncAreaVisuals() => _window.SyncAreaVisuals();
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public void BeginAreaMove(int start, int end) => _window.Arrangement.BeginAreaMove(start, end);
        public void ResetTrackListHeight() => _window.Arrangement.RequestResetTrackListHeight();
        public void Refresh(EditViews views) => _window.RefreshAfterEdit((EditRefresh)(int)views);
        public void AddSectionAt(int bar) => _window.AddSectionAt(bar);
        public void RenameSection(MarkerModel marker) => _window.EditSectionTitle(marker);
        public void GoToSection(MarkerModel marker) => _window.JumpToMarker(marker);

        public void ToggleTrackLines()
        {
            _window.Arrangement.ToggleTrackLines();
            _window.SaveTimelineAppearance();
        }

        public void PlaceCaretAfterOpen(int trackIndex, int bar) =>
            _window.Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
            {
                if (trackIndex >= 0 && _window.TrackMixerGrid.SelectedIndex != trackIndex) _window.TrackMixerGrid.SelectedIndex = trackIndex;
                if (bar >= 0) _window.Editor.SetBar(bar, seekPlayback: false);
            });

        public string[]? PickAudioFiles()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Audio files|" + string.Join(";", Audio.WaveformCache.Extensions.Select(x => "*" + x)) + "|All files|*.*",
                Multiselect = true
            };
            return dialog.ShowDialog(_window) == true ? dialog.FileNames : null;
        }
    }

    /// <summary>Right-click on the section lane: a section's menu, or just "Add section" on an empty stretch.</summary>
    private void ShowSectionLaneMenu(int markerIndex, int bar)
    {
        if (markerIndex >= 0) { ShowSectionContextMenu(markerIndex, bar); return; }
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            PlacementTarget = Arrangement,
            Placement = PlacementMode.MousePoint
        };
        var add = new MenuItem { Header = $"Add section at bar {bar + 1}", InputGestureText = HotkeyCatalog.DisplayAll(_settings.Hotkeys, "Section.Add"), Style = (Style)FindResource(typeof(MenuItem)) };
        add.Click += (_, _) => AddSectionAt(bar);
        menu.Items.Add(add);
        menu.IsOpen = true;
    }

    private void EditSectionTitle(MarkerModel marker)
    {
        var edited = GpDialogs.EditMarker(marker, Arrangement.MatchSimilarSectionColours);
        if (edited is null || SectionColours.IsUnchangedEdit(marker, edited.Value.title, edited.Value.color)) return;
        DocumentEdits.Run(Doc, project => SectionColours.ApplyEdit(project.Markers, marker, edited.Value.title, edited.Value.color, Arrangement.MatchSimilarSectionColours));
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Markers);
    }

    /// <summary>The view's half of a structural edit (see <see cref="FinishArrangementEdit"/>: the song was already marked changed, once, by the edit itself).</summary>
    private void FinishSectionStructureEdit(string status, int[] oldToNewBar, int? continueAtBar = null)
    {
        _selection.Remap(oldToNewBar, MaxMeasures());   // the selected range follows its bars
        if (Playback.ApplyStructureEdit(oldToNewBar, MaxMeasures()))
        {
            Editor.Playback.BarRemap = _playbackBarRemap;
            Playback.Engine.RefreshArrangement(_project, _playbackBarRemap!, continueAtBar);
        }
        Editor.InvalidateScoreLayout();
        RefreshArrangement(keepRows: true);
        RefreshMarkers();
        RefreshTabs();
        UpdateTitle();
        if (_isPlayingVisual && _playheadBar >= 0)
        {
            Editor.Playback.SetPlayhead(_playheadBar, _playheadCell);
            Playhead.SetGeometry(Editor.Playback.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.Playback.DurationGeometries());
        }
        else
        {
            _midi.Rebuild(_project);
        }
        StatusText.Text = status;
    }

    /// <summary>The window's side of <see cref="SectionEditFlow"/>.</summary>
    private sealed class SectionHost : ISectionEditHost
    {
        private readonly MainWindow _window;

        public SectionHost(MainWindow window)
        {
            _window = window;
            // The Delete key on selected whole bars without notes asks to remove the bars before it clears beats.
            window.Editor.BeforeDelete = () =>
            {
                var (m1, c1, m2, c2) = window.Editor.SelectionCellRange;
                return window.Editor.HasSelection && window._sections.TryDeleteSelectedEmpty(window.Doc, window.Editor.SelectedTrackIndex, m1, c1, m2, c2);
            };
        }

        public AppSettings Settings => _window._settings;
        Window IBarRangePromptHost.Owner => _window;
        public void SaveSettings() => _window.SaveSettings();
        public bool IsShown(DocumentSession document) => ReferenceEquals(document, _window.Doc);
        public ClipboardService Clipboard => ClipboardService.Shared;
        public IPasteQuestionAsker PasteQuestions => new WpfPasteQuestionAsker(_window);
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public int SelectedTrackIndex => Math.Max(0, _window.TrackMixerGrid.SelectedIndex);
        public EditorCaret Caret => new(_window.Editor.SelectedMeasure, _window.Editor.SelectedCell, _window.Editor.SelectedString);
        public void SetCaret(int bar, int cell, int stringIndex) => _window.Editor.SetPosition(bar, cell, stringIndex, seekPlayback: false);
        public void SelectBar(int bar) => _window.Editor.SetBar(bar);
        public void RemapSelection(int[] oldToNewBar) => _window._selection.Remap(oldToNewBar, _window.MaxMeasures());
        public void ClearSelection() => _window._selection.Clear(SelectionOrigin.Command);
        public void SelectBars(int start, int end) => _window.ApplyLoopRange(start, end);
        public void EndLoop() { if (_window._loop) _window.SetLoopActive(false); }
        public void SyncAreaVisuals() => _window.SyncAreaVisuals();
        public void SyncAudioEngine() => _window.SyncAudioEngine();

        public void FinishStructureEdit(string status, int[] oldToNewBar, int? continueAtBar) => _window.FinishSectionStructureEdit(status, oldToNewBar, continueAtBar);

        /// <summary>The view's half of an arrangement edit. The model half (undo step, dirty flag, timeline invalidation) was done once by <see cref="DocumentEdits"/>; nothing here marks the song again.</summary>
        public void FinishBarEdit(string status)
        {
            _window.Editor.InvalidateScoreLayout();
            _window.RefreshArrangement(keepRows: true);
            _window.RefreshTabs();
            _window.UpdateTitle();
            _window._midi.Rebuild(_window._project);
            _window.StatusText.Text = status;
        }

        public bool ConfirmWarning(string text, string caption) =>
            MessageBox.Show(_window, text, caption, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        public bool Ask(string title, string text, string yesText, bool withUndoHint) => ConfirmPrompt.Ask(_window, title, text, yesText, withUndoHint);


        public void ShowLastBarWarning() => MessageBox.Show(_window,
            "The last remaining bar cannot be removed from a song.", "Section not removed",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }
}
