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
        Arrangement.Bind(_project, MidiDevices);
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
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
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
        var currentBar = _playheadBar;
        var selectedBar = Editor.SelectedMeasure;
        var dragStart = _sectionUndoSnapshot;
        _sectionUndoSnapshot = null;
        var before = dragStart ?? _undo.Snapshot(_project);
        var mapping = SectionReorderService.Move(_project, from, insertBefore);
        if (mapping is null) return;
        var capture = _undo.Capture(before);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        _project.IsDirty = true;
        _project.MarkTimelineChanged();
        _selection.Remap(mapping, MaxMeasures());   // the selected range follows its bars
        if (selectedBar >= 0 && selectedBar < mapping.Length)
            Editor.SetPosition(mapping[selectedBar], Editor.SelectedCell, Editor.SelectedString, seekPlayback: false);
        if (_isPlayingVisual)
        {
            var previous = _playbackBarRemap ?? Enumerable.Range(0, mapping.Length).ToArray();
            _playbackBarRemap = SectionReorderService.ComposeBarRemap(previous, mapping);
            Editor.PlaybackBarRemap = _playbackBarRemap;
            if (currentBar >= 0 && currentBar < mapping.Length) _playheadBar = mapping[currentBar];
            var engine = Playback.Engine;
            var project = _project;
            var playbackRemap = _playbackBarRemap;
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

    private List<List<MeasureModel>>? _areaClipboard;
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

    private void AddAreaMenuItems(ContextMenu menu)
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        var label = s == e ? $"bar {s + 1}" : $"bars {s + 1}-{e + 1}";
        MenuItem Item(string header, Action action, bool enabled = true)
        {
            var item = new MenuItem { Header = header, Style = (Style)FindResource(typeof(MenuItem)), IsEnabled = enabled };
            item.Click += (_, _) => action();
            return item;
        }
        var index = 0;
        void Add(Control item) => menu.Items.Insert(index++, item);
        Add(new MenuItem { Header = $"Selected area: {label}", IsEnabled = false, Style = (Style)FindResource(typeof(MenuItem)) });
        Add(Item("Copy area", CopyArea));
        Add(Item("Cut area", () => { CopyArea(); DeleteArea("Cut"); }));
        Add(Item("Paste before area", () => PasteAreaAt(s), _areaClipboard is not null));
        Add(Item("Move area… (click the new position)", () => Arrangement.BeginAreaMove(s, e)));
        Add(Item("Delete area", () => DeleteArea("Deleted")));
        var loop = Item(_loop ? "Loop area (on)" : "Loop area", () => SetLoopActive(!_loop));
        loop.IsCheckable = true; loop.IsChecked = _loop;
        Add(loop);
        var skipped = _skipRanges.Any(r => r.Start == s && r.End == e);
        var skip = Item("Skip area during playback", () =>
        {
            if (skipped) _skipRanges.RemoveAll(r => r.Start == s && r.End == e);
            else _skipRanges.Add((s, e));
            SyncAreaVisuals();
            StatusText.Text = skipped ? $"Playing {label} again" : $"Skipping {label} during playback";
        });
        skip.IsCheckable = true; skip.IsChecked = skipped;
        Add(skip);
        if (_skipRanges.Count > 0) Add(Item("Play all skipped areas again", () => { _skipRanges.Clear(); SyncAreaVisuals(); }));
        Add(Item("Clear selection (Esc)", ClearLoopAreaKeepLoop));
        Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
    }

    private void CopyArea()
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        _areaClipboard = BarRangeEditor.Capture(_project, s, e);
        StatusText.Text = $"Copied bars {s + 1}-{e + 1}";
    }

    private void DeleteArea(string verb)
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        var transaction = _undo.BeginTransaction(_project);
        var map = BarRangeEditor.Remove(_project, s, e);
        if (map is null) { _undo.Cancel(transaction); ShowLastSectionWarning(); return; }
        var capture = _undo.Commit(transaction);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        if (_loop) SetLoopActive(false);
        _selection.Clear(SelectionOrigin.Command);   // score and timeline drop the deleted range together
        _skipRanges.Clear();
        SyncAreaVisuals();
        Editor.SetPosition(Math.Clamp(s, 0, Math.Max(0, MaxMeasures() - 1)), 0, Editor.SelectedString, seekPlayback: false);
        FinishSectionStructureEdit($"{verb} bars {s + 1}-{e + 1}", map, Math.Min(s, MaxMeasures() - 1));
    }

    private void PasteAreaAt(int at)
    {
        if (_areaClipboard is null) return;
        var transaction = _undo.BeginTransaction(_project);
        var map = BarRangeEditor.Insert(_project, at, _areaClipboard);
        var capture = _undo.Commit(transaction);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        var count = _areaClipboard.Max(t => t.Count);
        FinishSectionStructureEdit($"Pasted {count} bar(s) at bar {at + 1}", map);
        ApplyLoopRange(at, at + count - 1);   // after the remap: these are already new bar numbers
    }

    private void MoveAreaTo(int insertBefore)
    {
        var (s, e) = (_loopStartBar, _loopEndBar);
        if (insertBefore < 0 || (insertBefore >= s && insertBefore <= e + 1)) { StatusText.Text = "Move cancelled"; return; }
        var count = e - s + 1;
        var transaction = _undo.BeginTransaction(_project);
        if (BarRangeEditor.Move(_project, s, e, insertBefore) is not var (at, map)) { _undo.Cancel(transaction); return; }
        var capture = _undo.Commit(transaction);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        _skipRanges.Clear();
        FinishSectionStructureEdit($"Moved bars {s + 1}-{e + 1} to bar {at + 1}", map);
        ApplyLoopRange(at, at + count - 1);   // after the remap: these are already new bar numbers
        SyncAreaVisuals();
    }

    private void ShowArrangementContextMenu(int bar, int trackIndex)
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

        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (Brush)FindResource("Panel2Brush"),
            Foreground = (Brush)FindResource("TextBrush"),
            PlacementTarget = Arrangement,
            Placement = PlacementMode.MousePoint
        };
        System.Windows.Automation.AutomationProperties.SetName(menu, "Arrangement timeline options");
        MenuItem Item(string header, RoutedEventHandler handler, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = header,
                Style = (Style)FindResource(typeof(MenuItem)),
                IsEnabled = enabled
            };
            item.Click += handler;
            return item;
        }
        void Add(string header, Action action, bool enabled = true) => menu.Items.Add(Item(header, (_, _) => action(), enabled));
        void Sep() => menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });

        var individualNotes = new MenuItem
        {
            Header = "Show individual notes",
            Style = (Style)FindResource(typeof(MenuItem)),
            IsCheckable = true,
            IsChecked = Arrangement.ShowIndividualNotes
        };
        individualNotes.Click += (_, _) =>
        {
            Arrangement.ShowIndividualNotes = individualNotes.IsChecked;
            ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
            ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        };
        menu.Items.Add(individualNotes);
        var continuousLine = new MenuItem
        {
            Header = "Show continuous line",
            Style = (Style)FindResource(typeof(MenuItem)),
            IsCheckable = true,
            IsChecked = Arrangement.ShowContinuousBlocks
        };
        continuousLine.Click += (_, _) =>
        {
            Arrangement.ShowContinuousBlocks = continuousLine.IsChecked;
            ArrangementIndividualNotesMenu.IsChecked = Arrangement.ShowIndividualNotes;
            ArrangementContinuousBlocksMenu.IsChecked = Arrangement.ShowContinuousBlocks;
        };
        menu.Items.Add(continuousLine);
        var hideEmptyGrid = new MenuItem
        {
            Header = "Hide grid in empty bars",
            Style = (Style)FindResource(typeof(MenuItem)),
            IsCheckable = true,
            IsChecked = Arrangement.HideEmptyTimelineGrid
        };
        hideEmptyGrid.Click += (_, _) => Arrangement.HideEmptyTimelineGrid = hideEmptyGrid.IsChecked;
        menu.Items.Add(hideEmptyGrid);
        var barGlow = new MenuItem
        {
            Header = "Subtle bar glow",
            Style = (Style)FindResource(typeof(MenuItem)),
            IsCheckable = true,
            IsChecked = Arrangement.ShowBarGlow
        };
        barGlow.Click += (_, _) => Arrangement.ShowBarGlow = barGlow.IsChecked;
        menu.Items.Add(barGlow);
        Sep();

        var selectedTrack = SelectedTrack;
        var hasBar = bar >= 0;
        Add("Copy bar (this track)", () => CopyArrangementBar(bar, selectedTrack, allTracks: false), hasBar && selectedTrack is not null);
        Add("Copy bar (all tracks)", () => CopyArrangementBar(bar, selectedTrack, allTracks: true), hasBar && _project.Tracks.Count > 0);
        Add("Copy section", () => CopyArrangementSection(bar), hasBar && _arrangementController.SectionAt(_project, bar) is not null);
        var section = hasBar ? _arrangementController.SectionAt(_project, bar) : null;
        if (section is not null)
        {
            var lockPosition = Item("Lock section position", (_, _) =>
            {
                CaptureUndo();
                section.LockPosition = !section.LockPosition;
                CommitEdit(EditRefresh.Arrangement | EditRefresh.Markers);
            });
            lockPosition.IsCheckable = true;
            lockPosition.IsChecked = section.LockPosition;
            menu.Items.Add(lockPosition);
        }
        Sep();
        Add("Paste bar into this track", () => PasteArrangementBar(bar, selectedTrack, allTracks: false), hasBar && selectedTrack is not null && _arrangementController.HasSingleTrackBarClipboard);
        Add("Paste bar into all tracks", () => PasteArrangementBar(bar, selectedTrack, allTracks: true), hasBar && _arrangementController.HasAllTracksBarClipboard);
        Add("Paste section here", () => PasteArrangementSection(bar), hasBar && _arrangementController.HasSectionClipboard);
        Sep();
        Add("Add bar (in front)", () => InsertArrangementBar(bar), hasBar);
        Add("Add bar (behind)", () => InsertArrangementBar(bar + 1), hasBar);
        Sep();
        Add("Delete bar (this track)", () => DeleteArrangementBar(bar, selectedTrack, allTracks: false), hasBar && selectedTrack is not null && selectedTrack.Measures.Count > 1);
        Add("Delete bar (all tracks)", () => DeleteArrangementBar(bar, selectedTrack, allTracks: true), hasBar && MaxMeasures() > 1);
        if (AreaContains(bar)) AddAreaMenuItems(menu);
        menu.IsOpen = true;
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
        var menu = new ContextMenu
        {
            Style = (Style)FindResource(typeof(ContextMenu)),
            Background = (Brush)FindResource("Panel2Brush"),
            Foreground = (Brush)FindResource("TextBrush"),
            PlacementTarget = Arrangement,
            Placement = PlacementMode.MousePoint
        };
        System.Windows.Automation.AutomationProperties.SetName(menu, "Section options");

        MenuItem Item(string header, RoutedEventHandler handler, bool enabled = true)
        {
            var item = new MenuItem
            {
                Header = header,
                Style = (Style)FindResource(typeof(MenuItem)),
                IsEnabled = enabled
            };
            item.Click += handler;
            return item;
        }

        if (clickedBar is int atBar && atBar != marker.MeasureIndex)
        {
            menu.Items.Add(Item($"Add section at bar {atBar + 1}", (_, _) => AddSectionAt(atBar)));
            menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        }
        menu.Items.Add(Item("Copy Section", (_, _) => CopySectionToClipboard(marker)));
        menu.Items.Add(Item("Cut Section", (_, _) => CutArrangementSection(marker)));
        menu.Items.Add(Item("Paste Section", (_, _) => PasteArrangementSectionAfter(marker),
            _arrangementController.HasSectionClipboard));
        menu.Items.Add(Item("Duplicate Section", (_, _) => DuplicateArrangementSection(marker)));
        menu.Items.Add(Item("Delete Section", (_, _) => DeleteSectionContent(marker, confirm: true)));
        menu.Items.Add(new Separator { Style = (Style)FindResource(typeof(Separator)) });
        var sectionLastBar = Math.Max(marker.MeasureIndex, SectionLayout.End(markers, markerIndex, MaxMeasures()) - 1);
        var sectionLooped = _loop && _loopStartBar == marker.MeasureIndex && _loopEndBar == sectionLastBar;
        var loopItem = Item("Loop section", (_, _) =>
        {
            if (sectionLooped) { SetLoopActive(false); return; } // ticked: clicking again turns the loop off
            SetLoopActive(true);
            ApplyLoopRange(marker.MeasureIndex, sectionLastBar);
        });
        loopItem.IsCheckable = true;
        loopItem.IsChecked = sectionLooped;
        menu.Items.Add(loopItem);
        menu.Items.Add(Item("Go to section", (_, _) => JumpToMarker(marker)));
        var brackets = Item("Show section brackets [ ]", (_, _) =>
        {
            _settings.Timeline.ShowSectionBrackets = !_settings.Timeline.ShowSectionBrackets;
            Arrangement.ShowSectionBrackets = _settings.Timeline.ShowSectionBrackets;
            SaveSettings();
        });
        brackets.IsCheckable = true;
        brackets.IsChecked = _settings.Timeline.ShowSectionBrackets;
        menu.Items.Add(brackets);
        var similar = Item("Same colour for similar sections", (_, _) =>
        {
            _settings.Timeline.MatchSimilarSectionColours = !_settings.Timeline.MatchSimilarSectionColours;
            Arrangement.MatchSimilarSectionColours = _settings.Timeline.MatchSimilarSectionColours;
            SaveSettings();
        });
        similar.IsCheckable = true;
        similar.IsChecked = _settings.Timeline.MatchSimilarSectionColours;
        similar.ToolTip = "Verse 1, Verse 2… share the first one's colour. Display only; each section keeps its own colour.";
        menu.Items.Add(similar);
        menu.Items.Add(Item("Edit section title…", (_, _) => EditSectionTitle(marker)));
        var lockPosition = Item(marker.LockPosition ? "Unlock section position" : "Lock section position", (_, _) =>
        {
            CaptureUndo();
            marker.LockPosition = !marker.LockPosition;
            CommitEdit(EditRefresh.Arrangement | EditRefresh.Markers);
        });
        lockPosition.IsCheckable = true;
        lockPosition.IsChecked = marker.LockPosition;
        menu.Items.Add(lockPosition);
        menu.IsOpen = true;
    }

    private void EditSectionTitle(MarkerModel marker)
    {
        var edited = GpDialogs.Marker(marker.Title, marker.ColorHex, "Save");
        if (edited is null || (string.Equals(edited.Value.title, marker.Title, StringComparison.Ordinal) &&
                               string.Equals(edited.Value.color, marker.ColorHex, StringComparison.OrdinalIgnoreCase))) return;
        CaptureUndo();
        marker.Title = edited.Value.title;
        marker.ColorHex = edited.Value.color;
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement | EditRefresh.Markers);
    }

    private void CopyArrangementBar(int bar, TrackModel? track, bool allTracks)
    {
        var trackIndex = track is null ? -1 : _project.Tracks.IndexOf(track);
        if (!_arrangementController.CopyBar(_project, bar, trackIndex, allTracks)) return;
        if (allTracks)
            StatusText.Text = $"Copied bar {bar + 1} from all tracks";
        else if (track is not null)
            StatusText.Text = $"Copied bar {bar + 1} from {track.Name}";
    }

    private void CopyArrangementSection(int bar)
    {
        var marker = _arrangementController.SectionAt(_project, bar);
        if (marker is null) return;
        CopySectionToClipboard(marker);
    }

    private void CopySectionToClipboard(MarkerModel marker)
    {
        if (!_arrangementController.CopySection(_project, marker)) return;
        StatusText.Text = $"Copied section '{marker.Title}'";
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
        var snapshot = _arrangementController.SectionClipboard;
        if (snapshot is null || !_arrangementController.TryGetSectionBounds(_project, marker, out _, out var end)) return;
        InsertSectionSnapshot(end, snapshot, $"Pasted section '{snapshot.Marker.Title}'");
    }

    private void InsertSectionSnapshot(int at, SectionClipboardSnapshot snapshot, string status)
    {
        if (_project.Tracks.Count == 0 || snapshot.Tracks.Count == 0 || snapshot.Tracks.Max(track => track.Count) == 0) return;
        at = Math.Clamp(at, 0, MaxMeasures());
        var transaction = _undo.BeginTransaction(_project);
        var mapping = SectionReorderService.Insert(_project, at, snapshot.Tracks, snapshot.Marker);
        if (mapping is null) { _undo.Cancel(transaction); return; }
        var capture = _undo.Commit(transaction);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
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
                $"Delete the entire '{marker.Title}' section and its musical content from every track?",
                "Delete Section", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        var selectedBar = Editor.SelectedMeasure;
        var selectedCell = Editor.SelectedCell;
        var selectedString = Editor.SelectedString;
        var transaction = _undo.BeginTransaction(_project);
        var removal = SectionReorderService.Delete(_project, marker);
        if (removal is null) { _undo.Cancel(transaction); return; }
        var capture = _undo.Commit(transaction);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        var mappedSelection = selectedBar >= 0 && selectedBar < removal.OldToNewBar.Length
            ? removal.OldToNewBar[selectedBar]
            : -1;
        var newSelection = mappedSelection >= 0 ? mappedSelection : removal.ContinueAtBar;
        Editor.SetPosition(Math.Clamp(newSelection, 0, Math.Max(0, MaxMeasures() - 1)), selectedCell,
            selectedString, seekPlayback: false);
        FinishSectionStructureEdit(status ?? $"Deleted section '{marker.Title}'", removal.OldToNewBar,
            removal.ContinueAtBar);
    }

    private void ShowLastSectionWarning() => MessageBox.Show(this,
        "The last remaining bar cannot be removed from a song.", "Section not removed",
        MessageBoxButton.OK, MessageBoxImage.Information);

    private void PasteArrangementBar(int bar, TrackModel? track, bool allTracks)
    {
        var trackIndex = track is null ? -1 : _project.Tracks.IndexOf(track);
        if (!_arrangementController.CanPasteBar(_project, bar, trackIndex, allTracks)) return;
        CaptureUndo();
        _arrangementController.PasteBar(_project, bar, trackIndex, allTracks);
        FinishArrangementEdit($"Pasted bar {bar + 1}");
    }

    private void PasteArrangementSection(int bar)
    {
        var snapshot = _arrangementController.SectionClipboard;
        if (snapshot is null) return;
        InsertSectionSnapshot(Math.Clamp(bar, 0, MaxMeasures()), snapshot,
            $"Pasted section '{snapshot.Marker.Title}'");
    }

    private void InsertArrangementBar(int at)
    {
        CaptureUndo();
        var oldBars = MaxMeasures();
        at = _arrangementController.InsertBar(_project, at, at == 0 ? 0 : at - 1, moveMarkers: true);
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
            CaptureUndo();
            var oldBars = MaxMeasures();
            if (!_arrangementController.DeleteBar(_project, bar, -1, allTracks: true, moveMarkers: true)) return;
            if (MaxMeasures() < oldBars) _selection.Remap(SelectionModel.RemoveMap(oldBars, bar, bar), MaxMeasures());
        }
        else
        {
            if (track is null || track.Measures.Count <= 1 || bar >= track.Measures.Count) return;
            if (_settings.Editing.ConfirmDeleteBar && MessageBox.Show(this,
                    $"Delete bar {bar + 1} from {track.Name}?", "Delete bar", MessageBoxButton.YesNo,
                    MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
            CaptureUndo();
            var trackIndex = _project.Tracks.IndexOf(track);
            if (!_arrangementController.DeleteBar(_project, bar, trackIndex, allTracks: false, moveMarkers: false)) return;
        }
        Editor.SetBar(Math.Clamp(bar, 0, Math.Max(0, MaxMeasures() - 1)));
        FinishArrangementEdit($"Deleted bar {bar + 1}");
    }

    private void FinishArrangementEdit(string status)
    {
        _project.IsDirty = true;
        _project.MarkTimelineChanged();
        Editor.InvalidateScoreLayout();
        RefreshArrangement();
        RefreshTabs();
        UpdateTitle();
        _midi.Rebuild(_project);
        StatusText.Text = status;
    }

    private void FinishSectionStructureEdit(string status, int[] oldToNewBar, int? continueAtBar = null)
    {
        _project.IsDirty = true;
        _project.MarkTimelineChanged();
        _selection.Remap(oldToNewBar, MaxMeasures());   // the selected range follows its bars
        if (_isPlayingVisual)
        {
            var currentBar = _playheadBar;
            var previous = _playbackBarRemap ?? Enumerable.Range(0, oldToNewBar.Length).ToArray();
            _playbackBarRemap = SectionReorderService.ComposeBarRemapWithInsertions(previous, oldToNewBar,
                MaxMeasures());
            Editor.PlaybackBarRemap = _playbackBarRemap;
            if (currentBar >= 0 && currentBar < oldToNewBar.Length && oldToNewBar[currentBar] >= 0)
                _playheadBar = oldToNewBar[currentBar];
            Playback.Engine.RefreshArrangement(_project, _playbackBarRemap, continueAtBar);
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
