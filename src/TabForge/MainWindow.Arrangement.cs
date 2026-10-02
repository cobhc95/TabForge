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

    private void RefreshArrangement()
    {
        Arrangement.Bind(_project, MidiDevices, Doc.Media);
        Arrangement.SetSelectedBar(Editor.SelectedMeasure);
        Arrangement.SetSelectedTrack(Math.Max(0, TrackMixerGrid.SelectedIndex));
        // Every structural edit, undo/redo and track change ends here: the bar count may have changed.
        ReconcileSelection();
        SyncArrangementPlayhead();
        var (ls, le) = GetLoopRange();
        Arrangement.SetLoopRange(ls, le);
        Arrangement.SetLoopEnabled(ShowLoopOnTimeline);
    }

    private void RefreshTimelineOverviewGeometry() => Arrangement.RefreshTimelineGeometry();

    private void RefreshArrangementScore() => Arrangement.RefreshScore();

    private void RefreshArrangementSelection()
    {
        Arrangement.SetSelectedBar(Editor.SelectedMeasure);
        Arrangement.SetScoreSelection(_selection.BarRange);
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
        _project.IsDirty = true;
        _project.MarkTimelineChanged();
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
        _mixerWindow?.SyncValues();       // an open mixer follows the track list: values in place, rebuild only on structure change
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
    private void ApplyLoopRange(int start, int end, int startCell = 0, int endCell = -1) =>
        _selection.SetRange(Editor.SelectedTrackIndex, start, end, SelectionOrigin.Command, startCell, endCell);

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
            : $"Loop range set to bars {start + 1}-{end + 1} (F9 to play in loops)";
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
        if (selectedBar >= 0 && selectedBar < mapping.Length)
            Editor.SetPosition(mapping[selectedBar], Editor.SelectedCell, Editor.SelectedString, seekPlayback: false);
        if (Playback.ApplySectionMove(mapping))   // the document's remap and playhead bar; the views follow below
        {
            Editor.PlaybackBarRemap = _playbackBarRemap;
            var engine = Playback.Engine;
            var project = _project;
            var playbackRemap = _playbackBarRemap!;
            _ = Task.Run(() => engine.RefreshArrangement(project, playbackRemap));
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
        var previous = _scaleHighlight;
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
            TimelineClips.CanPasteOnTimeline(ClipboardService.Shared.TryGetClip(out _)), _loop, skipped, _skipRanges.Count > 0);
        return NewTimelineMenu("Arrangement timeline selection options", TimelineMenus.Selection(state, MenuKey), command =>
        {
            switch (command)
            {
                case TimelineCommand.TimelineSettings: OpenSettings(SettingsCatalog.Timeline, TimelineMenus.TimelineSettingsRow); break;
                case TimelineCommand.CopySelection: CopyArea(); break;
                case TimelineCommand.CutSelection: CopyArea(); DeleteArea("Cut"); break;
                case TimelineCommand.PasteSelection: PasteAreaAt(s); break;
                case TimelineCommand.DeleteSelection: DeleteArea("Deleted"); break;
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

    private void CopyArea()
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        CopyClipToClipboard(() => TimelineClips.CopyArea(_project, s, e), $"Copied bars {s + 1}-{e + 1}");
    }

    private void DeleteArea(string verb)
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        if (_arrangementController.DeleteBars(Doc, s, e).Value is not { } map) { ShowLastSectionWarning(); return; }
        if (_loop) SetLoopActive(false);
        _selection.Clear(SelectionOrigin.Command);   // score and timeline drop the deleted range together
        _skipRanges.Clear();
        SyncAreaVisuals();
        Editor.SetPosition(Math.Clamp(s, 0, Math.Max(0, MaxMeasures() - 1)), 0, Editor.SelectedString, seekPlayback: false);
        FinishSectionStructureEdit($"{verb} bars {s + 1}-{e + 1}", map, Math.Min(s, MaxMeasures() - 1));
    }

    private void PasteAreaAt(int at)
    {
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        TimelinePasteResult? answered = null;
        var paste = DocumentEdits.Run<TimelinePasteResult>(Doc, project =>   // one undo step for the whole paste
        {
            answered = TimelineClips.PasteBars(project, clip, at, Math.Max(0, TrackMixerGrid.SelectedIndex), TimelinePasteKind.InsertBars,
                _settings.Editing, new WpfPasteQuestionAsker(this));
            return answered.Changed && answered.OldToNewBar is not null ? answered : null;
        });
        SaveSettings();   // a "Remember my choice" answer
        if (paste.Value is not { } result) { StatusText.Text = answered?.Message ?? ""; return; }
        var count = result.BarsPasted;
        FinishSectionStructureEdit(result.Message, result.OldToNewBar!);
        ApplyLoopRange(at, at + count - 1);   // after the remap: these are already new bar numbers
    }

    private void MoveAreaTo(int insertBefore)
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        if (insertBefore < 0 || (insertBefore >= s && insertBefore <= e + 1)) { StatusText.Text = "Move cancelled"; return; }
        var count = e - s + 1;
        if (_arrangementController.MoveBars(Doc, s, e, insertBefore).Value is not { } moved) return;
        var (at, map) = (moved.At, moved.Map);
        _skipRanges.Clear();
        FinishSectionStructureEdit($"Moved bars {s + 1}-{e + 1} to bar {at + 1}", map);
        ApplyLoopRange(at, at + count - 1);   // after the remap: these are already new bar numbers
        SyncAreaVisuals();
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
        if (trackIndex >= 0 && trackIndex < _project.Tracks.Count)
            TrackMixerGrid.SelectedIndex = trackIndex;
        if (MaxMeasures() > 0)
        {
            bar = Math.Clamp(bar, 0, MaxMeasures() - 1);
            // Opening a timeline context menu only places the edit caret; it must never seek or
            // rebuild the engine as a side effect (notably for the section-lock menu item).
            Editor.SetBar(bar, seekPlayback: false);
        }
        else bar = -1;

        // Inside the selected bars: the selection menu only. Outside it: the single-bar menu, and the selection stays.
        Point? anchor = fromKeyboard && bar >= 0 ? Arrangement.TimelineBarAnchor(bar) : null;
        if (AreaContains(bar)) { OpenContextMenu(BuildSelectionMenu(), Arrangement, anchor, fromKeyboard); return; }

        var selectedTrack = SelectedTrack;
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
                case TimelineCommand.CopyBar: CopyArrangementBar(bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.CopyBarAllTracks: CopyArrangementBar(bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.CopySection: CopyArrangementSection(bar); break;
                case TimelineCommand.PasteBar: PasteArrangementBar(bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.PasteBarAllTracks: PasteArrangementBar(bar, selectedTrack, allTracks: true); break;
                case TimelineCommand.PasteSectionHere: PasteArrangementSection(bar); break;
                case TimelineCommand.InsertBarBefore: InsertArrangementBar(bar); break;
                case TimelineCommand.InsertBarAfter: InsertArrangementBar(bar + 1); break;
                case TimelineCommand.DeleteBar: DeleteArrangementBar(bar, selectedTrack, allTracks: false); break;
                case TimelineCommand.DeleteBarAllTracks: DeleteArrangementBar(bar, selectedTrack, allTracks: true); break;
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
        var add = new MenuItem { Header = $"Add section at bar {bar + 1}", InputGestureText = HotkeyCatalog.Display(HotkeyCatalog.GestureFor(_settings.Hotkeys, "Section.Add")), Style = (Style)FindResource(typeof(MenuItem)) };
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
                case TimelineCommand.CopySectionMenu: CopySectionToClipboard(marker); break;
                case TimelineCommand.CutSection: CutArrangementSection(marker); break;
                case TimelineCommand.PasteSectionAfter: PasteArrangementSectionAfter(marker); break;
                case TimelineCommand.DuplicateSection: DuplicateArrangementSection(marker); break;
                case TimelineCommand.DeleteSection: DeleteSectionContent(marker, confirm: true); break;
                case TimelineCommand.LoopSection:
                    if (sectionLooped) { SetLoopActive(false); break; } // ticked: clicking again turns the loop off
                    SetLoopActive(true);
                    ApplyLoopRange(marker.MeasureIndex, sectionLastBar);
                    break;
                case TimelineCommand.RenameSection: EditSectionTitle(marker); break;
                case TimelineCommand.GoToSection: JumpToMarker(marker); break;
                case TimelineCommand.ToggleSectionLock:
                    DocumentEdits.Run(Doc, _ => { marker.LockPosition = !marker.LockPosition; return true; });
                    RefreshAfterEdit(EditRefresh.Arrangement | EditRefresh.Markers);
                    break;
            }
        });
        menu.IsOpen = true;
    }

    private void EditSectionTitle(MarkerModel marker)
    {
        // Preselect the section's current colour: its own colour if valid, otherwise the colour the timeline shows.
        var currentHex = Visualization.ColourText.TryParse(marker.ColorHex, out _) ? marker.ColorHex
            : SectionColours.DisplayFor(marker) is { } shown ? $"#{shown.R:X2}{shown.G:X2}{shown.B:X2}" : "#2E74B5";
        var edited = GpDialogs.Marker(marker.Title, currentHex, "Save");
        if (edited is null || (string.Equals(edited.Value.title, marker.Title, StringComparison.Ordinal) &&
                               string.Equals(edited.Value.color, marker.ColorHex, StringComparison.OrdinalIgnoreCase))) return;
        DocumentEdits.Run(Doc, _ => { marker.Title = edited.Value.title; marker.ColorHex = edited.Value.color; return true; });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Markers);
    }

    private void CopyArrangementBar(int bar, TrackModel? track, bool allTracks)
    {
        var trackIndex = track is null ? -1 : _project.Tracks.IndexOf(track);
        if (!allTracks && track is null) return;
        CopyClipToClipboard(() => TimelineClips.CopyBar(_project, bar, trackIndex, allTracks),
            allTracks ? $"Copied bar {bar + 1} from all tracks" : $"Copied bar {bar + 1} from {track!.Name}");
    }

    /// <summary>Puts a Bars clip on the shared score clipboard (the same one the score editor pastes from) and reports it.</summary>
    private void CopyClipToClipboard(Func<ScoreClip> capture, string status)
    {
        try
        {
            var written = ClipboardService.Shared.Copy(capture());
            StatusText.Text = written ? status : status + " (clipboard busy: paste works inside TabForge only)";
        }
        catch (System.IO.InvalidDataException ex) { StatusText.Text = ex.Message; }
    }

    private static string WithNote(string status, string note) => note.Length > 0 ? $"{status} ({note})" : status;

    private void CopyArrangementSection(int bar)
    {
        var marker = _arrangementController.SectionAt(_project, bar);
        if (marker is null) return;
        CopySectionToClipboard(marker);
    }

    private void CopySectionToClipboard(MarkerModel marker)
    {
        try
        {
            if (_arrangementController.CopySection(_project, marker, ClipboardService.Shared, out var written) is null) return;
            StatusText.Text = written ? $"Copied section '{marker.Title}'" : $"Copied section '{marker.Title}' (clipboard busy: paste works inside TabForge only)";
        }
        catch (System.IO.InvalidDataException ex) { StatusText.Text = ex.Message; }
    }

    private void CutArrangementSection(MarkerModel marker)
    {
        if (!_arrangementController.CanDeleteSection(_project, marker, out _))
        {
            ShowLastSectionWarning();
            return;
        }
        CopySectionToClipboard(marker);
        DeleteSectionContent(marker, confirm: false, status: $"Cut section '{marker.Title}'");
    }

    private void DuplicateArrangementSection(MarkerModel marker)
    {
        var snapshot = _arrangementController.CaptureSectionSnapshot(_project, marker);
        if (snapshot is null || !_arrangementController.TryGetSectionBounds(_project, marker, out _, out var end)) return;
        InsertSectionSnapshot(end, snapshot,
            $"Duplicated section '{marker.Title}'");
    }

    private void PasteArrangementSectionAfter(MarkerModel marker)
    {
        if (!_arrangementController.TryGetSectionBounds(_project, marker, out _, out var end)) return;
        PasteClipAsSection(end);
    }

    private void InsertSectionSnapshot(int at, SectionClipboardSnapshot snapshot, string status)
    {
        at = Math.Clamp(at, 0, MaxMeasures());
        if (_arrangementController.InsertSection(Doc, at, snapshot).Value is not { } mapping) return;
        Editor.SetPosition(Math.Clamp(at, 0, Math.Max(0, MaxMeasures() - 1)), 0, Editor.SelectedString,
            seekPlayback: false);
        FinishSectionStructureEdit(status, mapping);
    }

    private void DeleteSectionContent(MarkerModel marker, bool confirm, string? status = null)
    {
        if (!_arrangementController.CanDeleteSection(_project, marker, out var start))
        {
            ShowLastSectionWarning();
            return;
        }
        if (confirm && _settings.General.ConfirmDeleteSection && MessageBox.Show(this,
                $"Delete the '{marker.Title}' section and its bars and notes from every track? Undo can restore them.",
                "Delete Section", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var selectedBar = Editor.SelectedMeasure;
        var selectedCell = Editor.SelectedCell;
        var selectedString = Editor.SelectedString;
        if (_arrangementController.DeleteSection(Doc, marker).Value is not { } removal) return;
        var mappedSelection = selectedBar >= 0 && selectedBar < removal.OldToNewBar.Length
            ? removal.OldToNewBar[selectedBar]
            : -1;
        var newSelection = mappedSelection >= 0 ? mappedSelection : removal.ContinueAtBar;
        Editor.SetPosition(Math.Clamp(newSelection, 0, Math.Max(0, MaxMeasures() - 1)), selectedCell,
            selectedString, seekPlayback: false);
        FinishSectionStructureEdit(status ?? $"Deleted section '{marker.Title}' and its bars from every track (Undo restores them)", removal.OldToNewBar,
            removal.ContinueAtBar);
    }

    private void ShowLastSectionWarning() => MessageBox.Show(this,
        "The last remaining bar cannot be removed from a song.", "Section not removed",
        MessageBoxButton.OK, MessageBoxImage.Information);

    private void PasteArrangementBar(int bar, TrackModel? track, bool allTracks)
    {
        var trackIndex = track is null ? -1 : _project.Tracks.IndexOf(track);
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        TimelinePasteResult? answered = null;
        var paste = DocumentEdits.Run<TimelinePasteResult>(Doc, project =>   // one undo step per paste
        {
            answered = TimelineClips.PasteBars(project, clip, bar, trackIndex, allTracks ? TimelinePasteKind.OverwriteAllTracks : TimelinePasteKind.OverwriteThisTrack,
                _settings.Editing, new WpfPasteQuestionAsker(this));
            return answered.Changed ? answered : null;
        });
        SaveSettings();   // a "Remember my choice" answer
        if (paste.Value is not { } result) { StatusText.Text = answered?.Message ?? ""; return; }
        if (result.OldToNewBar is { } map)   // answered "Insert before/after": structural, like the area paste
        {
            FinishSectionStructureEdit(result.Message, map);
            return;
        }
        FinishArrangementEdit(result.Message);
    }

    private void PasteArrangementSection(int bar)
    {
        PasteClipAsSection(Math.Clamp(bar, 0, MaxMeasures()));
    }

    /// <summary>
    /// Inserts the shared Bars clip before bar <paramref name="at"/> on all tracks: a copied section comes back with its title and
    /// colour; bars copied in the score or as an area come in as plain bars (no section marker).
    /// </summary>
    private void PasteClipAsSection(int at)
    {
        if (ClipboardService.Shared.TryGetClip(out var error) is not { } clip) { StatusText.Text = error ?? ClipboardService.NotTabForgeNotesMessage; return; }
        if (!TimelineClips.CanPasteOnTimeline(clip)) { StatusText.Text = "The timeline pastes whole bars only; paste beats in the score."; return; }
        var tracks = TimelineClips.BarsPerTrack(clip, _project, Math.Max(0, TrackMixerGrid.SelectedIndex), out var note);
        var marker = _arrangementController.SectionMarkerFor(clip);
        InsertSectionSnapshot(at, new SectionClipboardSnapshot(tracks, marker),
            WithNote(marker is not null ? $"Pasted section '{marker.Title}'" : $"Pasted {clip.BarCount} bar(s) at bar {at + 1}", note));
    }

    private void InsertArrangementBar(int at)
    {
        var oldBars = MaxMeasures();
        at = _arrangementController.InsertBar(Doc, at, at == 0 ? 0 : at - 1, moveMarkers: true).Value!.At;
        if (MaxMeasures() > oldBars) _selection.Remap(SelectionModel.InsertMap(oldBars, at), MaxMeasures());
        Editor.SetBar(Math.Min(at, Math.Max(0, MaxMeasures() - 1)));
        FinishArrangementEdit($"Added bar {at + 1}");
    }

    private void DeleteArrangementBar(int bar, TrackModel? track, bool allTracks)
    {
        if (bar < 0) return;
        if (allTracks)
        {
            if (MaxMeasures() <= 1) return;
            if (_settings.Editing.ConfirmDeleteBar && MessageBox.Show(this,
                    $"Delete bar {bar + 1} from every track?", "Delete bar", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var oldBars = MaxMeasures();
            if (!_arrangementController.DeleteBar(Doc, bar, -1, allTracks: true, moveMarkers: true).Changed) return;
            if (MaxMeasures() < oldBars) _selection.Remap(SelectionModel.RemoveMap(oldBars, bar, bar), MaxMeasures());
        }
        else
        {
            if (track is null || track.Measures.Count <= 1 || bar >= track.Measures.Count) return;
            if (_settings.Editing.ConfirmDeleteBar && MessageBox.Show(this,
                    $"Delete bar {bar + 1} from {track.Name}?", "Delete bar", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            var trackIndex = _project.Tracks.IndexOf(track);
            if (!_arrangementController.DeleteBar(Doc, bar, trackIndex, allTracks: false, moveMarkers: false).Changed) return;
        }
        Editor.SetBar(Math.Clamp(bar, 0, Math.Max(0, MaxMeasures() - 1)));
        FinishArrangementEdit($"Deleted bar {bar + 1}");
    }

    /// <summary>The view's half of an arrangement edit. The model half (undo step, dirty flag, timeline invalidation) was done once by <see cref="DocumentEdits"/>; nothing here marks the song again.</summary>
    private void FinishArrangementEdit(string status)
    {
        Editor.InvalidateScoreLayout();
        RefreshArrangement();
        RefreshTabs();
        UpdateTitle();
        _midi.Rebuild(_project);
        StatusText.Text = status;
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
        RefreshArrangement();
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
}
