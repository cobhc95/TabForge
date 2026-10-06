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
        MixerWindows.Mixer?.SyncValues();       // an open mixer follows the track list: values in place, rebuild only on structure change
        RefreshTabs();
        UpdateTitle();
    }

    private void CompleteTrackEditUndo()
    {
        if (_trackEditUndoTransaction is not { } transaction) return;
        var capture = _undo.Commit(transaction);
        if (capture.Stored) Playback.RememberBarMapping(capture.Snapshot);
        _trackEditUndoTransaction = null;
    }

    /// <summary>
    /// Selects bars start…end in both views; the selection is the loop area (applied by the model's observer).
    /// </summary>
    private void ApplyLoopRange(int start, int end, int startCell = 0, int endCell = -1, SelectionScope? scope = null) =>
        _selection.SetRange(Editor.SelectedTrackIndex, start, end, SelectionOrigin.Command, startCell, endCell, scope);

    /// <summary>Only <see cref="ApplySelectionToTimeline"/> calls this: the area always equals the shared selection.</summary>
    private void ApplyLoopArea(int start, int end, int startCell, int endCell)
    {
        _loopStartBar = start;
        _loopEndBar = end;
        _loopStartCell = Math.Max(0, startCell);
        _loopEndCell = endCell;
        _loopHasArea = true;
        Arrangement.SetLoopRange(start, end);
        SyncAreaVisuals();
        _midi.SetLoopRange(start, end, _loopStartCell, _loopEndCell);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        StatusText.Text = _loop
            ? $"Looping bars {start + 1}-{end + 1}"
            : BarRangePromptText.Tip(start, end, MenuKey);
    }

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
        var dragStart = _sectionUndoSnapshot;
        _sectionUndoSnapshot = null;
        if (_arrangementController.MoveSection(Doc, from, insertBefore, dragStart).Value is not { } mapping) return;   // one undo step, one dirty change, one timeline invalidation
        _selection.Remap(mapping, MaxMeasures());   // the selected range follows its bars
        SyncAreaVisuals();   // the skipped areas moved with them (in the controller)
        if (selectedBar >= 0 && selectedBar < mapping.Length)
            Editor.SetPosition(mapping[selectedBar], Editor.SelectedCell, Editor.SelectedString, seekPlayback: false);
        if (Playback.ApplySectionMove(mapping))   // the document's remap and playhead bar; the views follow below
        {
            Editor.PlaybackBarRemap = _playbackBarRemap;
            var engine = Playback.Engine;
            _ = engine.RefreshArrangementInBackground(_project, _playbackBarRemap!);   // copies the song here; the pool thread compiles the copy
        }
        Editor.InvalidateScoreLayout();
        Arrangement.RefreshSectionOrder(Editor.SelectedMeasure, Math.Max(0, TrackMixerGrid.SelectedIndex));
        RefreshMarkers();
        UpdateTitle();
        if (_isPlayingVisual && _playheadBar >= 0)
        {
            Editor.SetPlayhead(_playheadBar, _playheadCell);
            Playhead.SetGeometry(Editor.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.PlaybackDurationGeometries());
        }
        SyncAudioEngine();   // clips that moved with the section
        StatusText.Text = "Section moved";
    }

    private void UpdatePlayingSectionMarker(int bar, bool forceRefresh = false)
    {
        var current = _isPlayingVisual && bar >= 0
            ? SectionLayout.At(_project, bar)
            : null;
        var changed = !ReferenceEquals(current, _playingSectionMarker);
        if (!changed && !forceRefresh) return;

        _playingSectionMarker = current;
        if (current is not null)
        {
            _syncingPlayingSectionSelection = true;
            MarkerList.SelectedItem = current;
            _syncingPlayingSectionSelection = false;
            if (changed) MarkerList.ScrollIntoView(current);
        }
    }

    private void RefreshSongStats()
    {
        var notes = _project.Tracks.Sum(t => t.Measures.Sum(m => m.Cells.Sum(c => c.Notes.Count)));
        var maxM = _project.Tracks.Count == 0 ? 0 : _project.Tracks.Max(t => t.Measures.Count);
        SongStatsText.Text = $"Tracks {_project.Tracks.Count}   Bars {maxM}   Notes {notes}   Sections {_project.Markers.Count}";
        OverviewTitle.Text = string.IsNullOrWhiteSpace(_currentPath)
            ? $"{_project.Title} (unsaved)"
            : $"{_project.Title} — {Path.GetFileName(_currentPath)}";
    }

    private void RefreshTheoryCombos()
    {
        foreach (var n in MusicTheoryService.NoteNames) { ChordRootCombo.Items.Add(n); ScaleRootCombo.Items.Add(n); }
        foreach (var k in MusicTheoryService.Chords.Keys) ChordTypeCombo.Items.Add(k);
        foreach (var k in MusicTheoryService.Scales.Keys) ScaleNameCombo.Items.Add(k);
        ChordRootCombo.SelectedIndex = 0; ChordTypeCombo.SelectedIndex = 0;
        ScaleRootCombo.SelectedIndex = 0; ScaleNameCombo.SelectedIndex = 0;
    }

    private void RefreshScaleHighlightCombo()
    {
        var previous = InstrumentPane.ScaleHighlight;
        ScaleHighlightCombo.Items.Clear();
        ScaleHighlightCombo.Items.Add("Off");
        foreach (var root in MusicTheoryService.NoteNames)
            foreach (var scale in new[] { "Major", "Natural Minor", "Minor Pentatonic", "Major Pentatonic", "Dorian", "Mixolydian", "Blues" })
                ScaleHighlightCombo.Items.Add($"{root} {scale}");
        if (previous is not null && ScaleHighlightCombo.Items.Contains(previous)) ScaleHighlightCombo.SelectedItem = previous;
        else ScaleHighlightCombo.SelectedIndex = 0;
    }


    // ---------- selected area (bar range picked by dragging the timeline or selecting in the score) ----------

    private List<(int Start, int End)> _skipRanges => Doc.SkipRanges;

    private bool AreaContains(int bar) => _loopHasArea && bar >= _loopStartBar && bar <= _loopEndBar;

    /// <summary>
    /// The timeline marks the loop only when it is a real selection (an area, or the section loop). Looping
    /// the whole song needs no highlight over every bar: playback simply returns to bar 1 at the end.
    /// </summary>
    private bool ShowLoopOnTimeline
    {
        get
        {
            if (!_loop) return false;
            if (_loopHasArea) return true;
            var (start, end) = GetLoopRange();
            return start > 0 || end < Math.Max(0, MaxMeasures() - 1);
        }
    }

    /// <summary>Drops the selected area; an active loop falls back to its no-area range (the whole song).</summary>
    private void ClearLoopAreaKeepLoop() => _selection.Clear(SelectionOrigin.Command);

    private void SyncAreaVisuals()
    {
        Arrangement.SetAreaVisible(_loopHasArea);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
        Arrangement.SetSkipRanges(_skipRanges);
        _midi.SetSkipRanges(_skipRanges);
    }

    /// <summary>The selection menu (owner request: Copy / Cut / Paste / Delete on top, the rest in submenus).</summary>
    private ContextMenu BuildSelectionMenu()
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        var label = s == e ? $"bar {s + 1}" : $"bars {s + 1}-{e + 1}";
        var skipped = _skipRanges.Any(r => r.Start == s && r.End == e);
        var state = new SelectionMenuState(s == e ? $"Bar {s + 1} selected" : $"Bars {s + 1}-{e + 1} selected",
            TimelineClips.CanPasteOnTimeline(ClipboardService.Shared.TryGetClip(out _)), _loop, skipped, _skipRanges.Count > 0,
            EmptyBars.InRange(_project, s, e).Count);
        return NewTimelineMenu("Arrangement timeline selection options", TimelineMenus.Selection(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.TimelineSettings: OpenSettings(SettingsCatalog.Timeline, TimelineMenus.TimelineSettingsRow); break;
                case TimelineCommand.CopySelection: _sections.CopyArea(Doc, s, e, _selection.ScopeTrack); break;
                case TimelineCommand.CutSelection: _sections.CutArea(Doc, s, e, _selection.ScopeTrack); break;
                case TimelineCommand.PasteSelection: _sections.PasteAreaAt(Doc, s); break;
                case TimelineCommand.DeleteSelection: _sections.Range.Delete(Doc, s, e, _selection.Scope == SelectionScope.AllTracks); break;
                case TimelineCommand.DeleteEmptyBars: _sections.DeleteEmptyInRange(Doc, s, e); break;
                case TimelineCommand.LoopSelection: SetLoopActive(!_loop); break;
                case TimelineCommand.MoveSelection: Arrangement.BeginAreaMove(s, e); break;
                case TimelineCommand.SkipSelection:
                    if (skipped) _skipRanges.RemoveAll(r => r.Start == s && r.End == e);
                    else _skipRanges.Add((s, e));
                    SyncAreaVisuals();
                    StatusText.Text = skipped ? $"Playing {label} again" : $"Skipping {label} during playback";
                    break;
                case TimelineCommand.PlaySkippedAgain: _skipRanges.Clear(); SyncAreaVisuals(); break;
                case TimelineCommand.ClearSelection: ClearLoopAreaKeepLoop(); break;
            }
        });
    }

    /// <summary>
    /// Shift+F10 / the Menu key on the timeline: the selection menu when bars are selected, otherwise the bar menu for the playhead's bar
    /// (while playing) or the current bar of the selected track, at that bar's top-left with the first item focused.
    /// </summary>
    private void ShowTimelineContextMenuFromKeyboard()
    {
        if (MaxMeasures() == 0) return;
        var bar = _loopHasArea ? _loopStartBar
            : _isPlayingVisual && _playheadBar >= 0 ? _playheadBar : Editor.SelectedMeasure;
        ShowArrangementContextMenu(bar, Math.Max(0, TrackMixerGrid.SelectedIndex), fromKeyboard: true);
    }

    private void ShowArrangementContextMenu(int bar, int trackIndex, bool fromKeyboard = false)
    {
        using var slowTrace = TabForge.Views.SlowTrace.Measure("timeline menu build+open", 0);
        var hasTrack = trackIndex >= 0 && trackIndex < _project.Tracks.Count;
        bar = MaxMeasures() > 0 ? Math.Clamp(bar, 0, MaxMeasures() - 1) : -1;
        // The menu opens first; the track switch and caret placement (score relayout) follow once it is on screen.
        // Placing the caret never seeks or rebuilds the engine (notably for the section-lock menu item).
        var caretBar = bar;
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () =>
        {
            if (hasTrack && TrackMixerGrid.SelectedIndex != trackIndex) TrackMixerGrid.SelectedIndex = trackIndex;
            if (caretBar >= 0) Editor.SetBar(caretBar, seekPlayback: false);
        });

        // Inside the selected bars: the selection menu only. Outside it: the single-bar menu, and the selection stays.
        Point? anchor = fromKeyboard && bar >= 0 ? Arrangement.TimelineBarAnchor(bar) : null;
        if (AreaContains(bar)) { OpenContextMenu(BuildSelectionMenu(), Arrangement, anchor, fromKeyboard); return; }

        var selectedTrack = hasTrack ? _project.Tracks[trackIndex] : SelectedTrack;
        var hasBar = bar >= 0;
        var section = hasBar ? _arrangementController.SectionAt(_project, bar) : null;
        var barsClip = ClipboardService.Shared.TryGetClip(out _);
        var canPasteBars = TimelineClips.CanPasteOnTimeline(barsClip);
        var state = new BarMenuState(hasBar, selectedTrack is not null, _project.Tracks.Count,
            selectedTrack is not null && selectedTrack.Measures.Count > 1, MaxMeasures() > 1,
            section is not null, section?.LockPosition ?? false, canPasteBars, canPasteBars && barsClip!.Tracks.Count > 1);
        var menu = NewTimelineMenu("Arrangement timeline options", TimelineMenus.Bar(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.TimelineSettings: OpenSettings(SettingsCatalog.Timeline, TimelineMenus.TimelineSettingsRow); break;
                case TimelineCommand.CopyBar: _sections.CopyBar(Doc, bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.CopyBarAllTracks: _sections.CopyBar(Doc, bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.CopySection: _sections.CopySectionAt(Doc, bar); break;
                case TimelineCommand.PasteBar: _sections.PasteBar(Doc, bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.PasteBarAllTracks: _sections.PasteBar(Doc, bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.PasteSectionHere: _sections.PasteSectionAt(Doc, bar); break;
                case TimelineCommand.InsertBarBefore: _sections.InsertBar(Doc, bar); break;
                case TimelineCommand.InsertBarAfter: _sections.InsertBar(Doc, bar + 1); break;
                case TimelineCommand.DeleteBar or TimelineCommand.DeleteBarAllTracks: _sections.Range.Delete(Doc, bar, bar); break;   // the same prompt as a selection
                case TimelineCommand.ToggleSectionLockAtBar when section is not null:
                    DocumentEdits.Run(Doc, _ => { section.LockPosition = !section.LockPosition; return true; });
                    RefreshAfterEdit(EditRefresh.Arrangement | EditRefresh.Markers);
                    break;
            }
        });
        OpenContextMenu(menu, Arrangement, anchor, fromKeyboard);
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

    private void ShowSectionContextMenu(int markerIndex, int? clickedBar = null)
    {
        var markers = _project.Markers.OrderBy(marker => marker.MeasureIndex).ToList();
        if (markerIndex < 0 || markerIndex >= markers.Count) return;
        var marker = markers[markerIndex];
        var sectionLastBar = Math.Max(marker.MeasureIndex, SectionLayout.End(markers, markerIndex, MaxMeasures()) - 1);
        var sectionLooped = _loop && _loopStartBar == marker.MeasureIndex && _loopEndBar == sectionLastBar;
        int? addAt = clickedBar is int atBar && atBar != marker.MeasureIndex ? atBar : null;
        var state = new SectionMenuState(addAt, TimelineClips.CanPasteOnTimeline(ClipboardService.Shared.TryGetClip(out _)),
            sectionLooped, marker.LockPosition);
        var menu = NewTimelineMenu("Section options", TimelineMenus.Section(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.SectionSettings: OpenSettings(SettingsCatalog.Timeline, TimelineMenus.SectionSettingsRow); break;
                case TimelineCommand.AddSectionHere when addAt is int at: AddSectionAt(at); break;
                case TimelineCommand.CopySectionMenu: _sections.CopySection(Doc, marker); break;
                case TimelineCommand.CutSection: _sections.CutSection(Doc, marker); break;
                case TimelineCommand.PasteSectionAfter: _sections.PasteSectionAfter(Doc, marker); break;
                case TimelineCommand.DuplicateSection: _sections.DuplicateSection(Doc, marker); break;
                case TimelineCommand.DeleteSection: _sections.DeleteSection(Doc, marker, confirm: true); break;
                case TimelineCommand.LoopSection:
                    if (sectionLooped) { SetLoopActive(false); break; } // ticked: clicking again turns the loop off
                    SetLoopActive(true);
                    ApplyLoopRange(marker.MeasureIndex, sectionLastBar, scope: SelectionScope.AllTracks);
                    break;
                case TimelineCommand.RenameSection: EditSectionTitle(marker); break;
                case TimelineCommand.GoToSection: JumpToMarker(marker); break;
                case TimelineCommand.ToggleSectionLock:
                    DocumentEdits.Run(Doc, _ => { marker.LockPosition = !marker.LockPosition; return true; });
                    RefreshAfterEdit(EditRefresh.Arrangement | EditRefresh.Markers);
                    break;
            }
        });
        OpenContextMenu(menu, Arrangement, null, fromKeyboard: false);
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
            Editor.PlaybackBarRemap = _playbackBarRemap;
            Playback.Engine.RefreshArrangement(_project, _playbackBarRemap!, continueAtBar);
        }
        Editor.InvalidateScoreLayout();
        RefreshArrangement(keepRows: true);
        RefreshMarkers();
        RefreshTabs();
        UpdateTitle();
        if (_isPlayingVisual && _playheadBar >= 0)
        {
            Editor.SetPlayhead(_playheadBar, _playheadCell);
            Playhead.SetGeometry(Editor.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.PlaybackDurationGeometries());
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
