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

// MainWindow, score editing commands: undo, bars, measures, notes, effects, markers.
public partial class MainWindow
{
    // ---------- undo ----------

    private UndoCapture? CaptureUndo()
    {
        if (_restoring) return null;
        var capture = _undo.Capture(_project);
        if (capture.Stored) RememberPlaybackBarMapping(capture.Snapshot);
        return capture;
    }

    private void RememberPlaybackBarMapping(UndoSnapshot snapshot)
    {
        if (!_isPlayingVisual || _playbackBarRemap is null) return;
        _playbackBarMappingsBySnapshot[snapshot.Fingerprint] = _playbackBarRemap.ToArray();
    }

    private void ApplyRestoredPlaybackBarMapping(UndoSnapshot snapshot)
    {
        if (!_isPlayingVisual) return;
        var previous = _playbackBarRemap ?? Enumerable.Range(0, MaxMeasures()).ToArray();
        var restored = _playbackBarMappingsBySnapshot.TryGetValue(snapshot.Fingerprint, out var saved)
            ? saved.ToArray()
            : previous.ToArray();
        var priorLiveBar = _playheadBar;
        var sourceBar = Playback.Engine.Playhead().Bar;
        if (sourceBar < 0 || sourceBar >= restored.Length)
            sourceBar = Array.IndexOf(previous, _playheadBar);
        if (sourceBar >= restored.Length)
        {
            var previousLength = restored.Length;
            Array.Resize(ref restored, sourceBar + 1);
            Array.Fill(restored, -1, previousLength, restored.Length - previousLength);
        }
        _playbackBarRemap = restored;
        Editor.PlaybackBarRemap = restored;
        if (sourceBar >= 0 && sourceBar < restored.Length)
        {
            _playheadBar = restored[sourceBar] >= 0
                ? restored[sourceBar]
                : Math.Clamp(priorLiveBar, 0, Math.Max(0, MaxMeasures() - 1));
            Editor.SetPlayhead(_playheadBar, _playheadCell);
            Arrangement.SetPlayhead(_playheadBar, _playheadFraction, playbackActive: true, playbackPaused: _midi.IsPaused);
            Playhead.SetGeometry(Editor.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.PlaybackDurationGeometries());
        }
        UpdatePlayingSectionMarker(_playheadBar, forceRefresh: true);
        Playback.Engine.RefreshArrangement(_project, restored, Math.Clamp(priorLiveBar, 0, Math.Max(0, MaxMeasures() - 1)));
        RememberPlaybackBarMapping(snapshot);
    }

    /// <summary>Re-bases saved edit-state mappings when the engine compiles a new current-score timeline.</summary>
    private void RebasePlaybackBarMappings()
    {
        if (!_isPlayingVisual)
        {
            _playbackBarRemap = null;
            Editor.PlaybackBarRemap = null;
            _playbackBarMappingsBySnapshot.Clear();
            return;
        }

        var previous = _playbackBarRemap ?? Enumerable.Range(0, MaxMeasures()).ToArray();
        var currentCount = MaxMeasures();
        foreach (var entry in _playbackBarMappingsBySnapshot.ToArray())
            _playbackBarMappingsBySnapshot[entry.Key] = SectionReorderService.RebaseBarRemap(previous, entry.Value, currentCount);

        _playbackBarRemap = Enumerable.Range(0, currentCount).ToArray();
        Editor.PlaybackBarRemap = _playbackBarRemap;
        RememberPlaybackBarMapping(_undo.Snapshot(_project));
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undo.CanUndo) return;
        var current = _undo.Snapshot(_project);
        if (!_undo.TryUndo(current, out var target)) return;
        RememberPlaybackBarMapping(current);
        RestoreSnapshot(target);
        StatusText.Text = "Undo";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undo.CanRedo) return;
        var current = _undo.Snapshot(_project);
        if (!_undo.TryRedo(current, out var target)) return;
        RememberPlaybackBarMapping(current);
        RestoreSnapshot(target);
        StatusText.Text = "Redo";
    }

    private void RestoreSnapshot(UndoSnapshot snapshot)
    {
        var selected = TrackMixerGrid.SelectedIndex;
        _restoring = true;
        try
        {
            // Unchanged bars move over from the song being replaced; only the bars the undo changes are rebuilt.
            _project = _undo.Restore(snapshot, _project);
            _project.IsDirty = true;
            _project.MarkTimelineChanged();   // A5-08: undo/redo may restore in place
            // Undoing back to the saved state clears the "*": the exact content check runs here only.
            if (Doc.IsCleanContent(snapshot)) _project.IsDirty = false;
            TempoBox.Text = _project.Tempo.ToString();
            LyricsBox.Text = _project.Lyrics ?? "";
            Editor.Project = _project;
            RefreshTracks();
            if (_project.Tracks.Count > 0) TrackMixerGrid.SelectedIndex = Math.Clamp(selected, 0, _project.Tracks.Count - 1);
            RefreshPluginChain(); RefreshArrangement(); RefreshMarkers(); RefreshInstrument(); RefreshStatus(); UpdateTitle();
            ScheduleFitTimelineToTracks();
            RefreshToolsPalette();
            ApplyRestoredPlaybackBarMapping(snapshot);
        }
        finally { _restoring = false; }
    }


    // ---------- edit ----------

    private void InsertBeat_Click(object sender, RoutedEventArgs e) { Editor.InsertBeat(); StatusText.Text = "Inserted beat"; }
    private void DeleteBeats_Click(object sender, RoutedEventArgs e) { Editor.DeleteBeats(); StatusText.Text = "Deleted beats"; }
    // Same command as the C shortcut (EditCommands.CopyLastBeat through the editor): one behaviour, one undo step.
    private void CopyBeats_Click(object sender, RoutedEventArgs e) => Editor.CopyLastBeat();

    // ---------- measure ----------

    private void InsertBar_Click(object sender, RoutedEventArgs e)
    {
        CaptureUndo();
        var at = Math.Clamp(Editor.SelectedMeasure, 0, MaxMeasures());
        _arrangementController.InsertBar(_project, at, Editor.SelectedMeasure, moveMarkers: false);
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement); StatusText.Text = $"Inserted bar {at + 1}";
    }

    /// <summary>Adds an empty bar after the last one (the standard "Add bar"), keeping the cursor where it is.</summary>
    private void AppendBar()
    {
        CaptureUndo();
        var at = MaxMeasures();
        _arrangementController.InsertBar(_project, at, Math.Max(0, at - 1), moveMarkers: false);
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement);
        StatusText.Text = $"Added bar {at + 1} at the end";
    }

    /// <summary>
    /// The standard step buttons: move one beat. While playing it steps playback too, landing on the
    /// beat so you can walk through a passage; otherwise it just moves the edit cursor.
    /// </summary>
    private void StepBeat(int direction)
    {
        var playing = _midi.IsPlaying;
        Editor.MoveBeat(direction);
        ScrollToCursor();
        if (playing) _midi.Seek(_project, Editor.SelectedMeasure, Editor.SelectedCell);
        StatusText.Text = $"Bar {Editor.SelectedMeasure + 1}, beat {Editor.SelectedCell + 1}";
    }

    private void DeleteBar_Click(object sender, RoutedEventArgs e)
    {
        if (MaxMeasures() <= 1) { StatusText.Text = "Cannot delete the last bar"; return; }
        if (_settings.Editing.ConfirmDeleteBar && MessageBox.Show(this,
                $"Delete bar {Editor.SelectedMeasure + 1} from every track?",
                "Delete bar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            return;
        CaptureUndo();
        _arrangementController.DeleteBar(_project, Editor.SelectedMeasure, -1, allTracks: true, moveMarkers: false);
        _project.IsDirty = true; _project.MarkTimelineChanged(); Editor.SetPosition(Math.Max(0, Editor.SelectedMeasure - 1), 0, Editor.SelectedString);
        RefreshArrangement(); Editor.InvalidateScoreLayout(); UpdateTitle(); StatusText.Text = "Deleted bar";
    }

    private int MaxMeasures() => BarRangeEditor.MaxMeasures(_project);

    private MeasureModel? CurBar()
    {
        var t = SelectedTrack;
        if (t is null || Editor.SelectedMeasure >= t.Measures.Count) return null;
        return t.Measures[Editor.SelectedMeasure];
    }

    private void ProjectSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_project.Lyrics != LyricsBox.Text) _project.Lyrics = LyricsBox.Text;
        var r = Views.ProjectSettingsWindow.Show(this, _project);
        if (r is null) return;
        CaptureUndo();
        _project.Title = r.Title; _project.Subtitle = r.Subtitle; _project.Artist = r.Artist; _project.Album = r.Album;
        _project.MusicAuthor = r.MusicAuthor; _project.LyricsAuthor = r.LyricsAuthor;
        _project.TabAuthor = r.TabAuthor; _project.Copyright = r.Copyright;
        _project.Instructions = r.Instructions; _project.Notice = r.Notice;
        _project.Lyrics = r.Lyrics; LyricsBox.Text = r.Lyrics;
        _project.GrayInactiveVoice = r.GrayInactiveVoice;
        _project.Tempo = r.Tempo; TempoBox.Text = r.Tempo.ToString();
        if (r.TimeSigNum != _project.TimeSignatureNumerator || r.TimeSigDenom != _project.TimeSignatureDenominator)
            _arrangementController.SetTimeSignature(_project, 0, r.TimeSigNum, r.TimeSigDenom);
        if (r.KeySignature != _project.KeySignature || r.KeyMinor != _project.KeySignatureMinor)
            _arrangementController.SetKeySignature(_project, 0, r.KeySignature, r.KeyMinor);
        CommitEdit(EditRefresh.Score | EditRefresh.TimelineGeometry | EditRefresh.Palette | EditRefresh.Status);
        StatusText.Text = "Project settings updated";
    }

    private void TimeSig_Click(object sender, RoutedEventArgs e)
    {
        var current = CurBar();
        var r = GpDialogs.TimeSignature(current?.TimeSigNum ?? _project.TimeSignatureNumerator,
            current?.TimeSigDenom ?? _project.TimeSignatureDenominator);
        if (r is null) return;
        CaptureUndo();
        _arrangementController.SetTimeSignature(_project, Editor.SelectedMeasure, r.Value.num, r.Value.denom);
        CommitEdit(EditRefresh.Score | EditRefresh.TimelineGeometry | EditRefresh.Palette | EditRefresh.Status); StatusText.Text = $"Time signature {r.Value.num}/{r.Value.denom}";
    }

    private void KeySig_Click(object sender, RoutedEventArgs e)
    {
        var current = CurBar();
        var r = GpDialogs.KeySignature(current?.KeySignature ?? _project.KeySignature,
            current?.KeySignatureMinor ?? _project.KeySignatureMinor);
        if (r is null) return;
        CaptureUndo();
        _arrangementController.SetKeySignature(_project, Editor.SelectedMeasure, r.Value.signature, r.Value.minor);
        CommitEdit(EditRefresh.Score | EditRefresh.Palette | EditRefresh.Status);
    }

    private void Clef_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryCycleClef(_project, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out var clef)) return;
        CommitEdit(EditRefresh.Score); StatusText.Text = $"Clef {clef}";
    }

    private void TripletFeel_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryToggleTripletFeel(_project, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out var v)) return;
        CommitEdit(EditRefresh.None); StatusText.Text = v ? "Triplet feel on" : "Triplet feel off";
    }

    // Repeat open/close share EditCommands with the [ and ] shortcuts (through the editor): the same bar change in every
    // track and one undo step. The menu only adds the count prompt when a repeat end is being added.
    private void RepeatOpen_Click(object sender, RoutedEventArgs e) => Editor.ToggleRepeatOpen();

    private void RepeatClose_Click(object sender, RoutedEventArgs e)
    {
        var bar = CurBar(); if (bar is null) return;
        if (bar.RepeatEnd) { Editor.ToggleRepeatClose(); StatusText.Text = "Repeat end removed"; return; }
        var txt = GpDialogs.Prompt("Repeat close", "Repeat times (2-99):", bar.RepeatCount.ToString());
        if (txt is null) return;
        var n = Math.Clamp(int.TryParse(txt, out var v) ? v : 2, 2, TabForge.Playback.PlaybackOrder.MaxRepeats);
        Editor.ToggleRepeatClose(n);
        StatusText.Text = $"Repeat ×{n}";
    }

    private void Directions_Click(object sender, RoutedEventArgs e)
    {
        var bar = CurBar(); if (bar is null) return;
        var txt = GpDialogs.Directions(bar.Directions, bar.AlternateEnding, out var ending);
        if (txt is null) return;
        CaptureUndo();
        _arrangementController.TrySetDirections(_project, Editor.SelectedMeasure, txt, ending);
        CommitEdit(EditRefresh.Score | EditRefresh.Palette);
    }

    private void DoubleBar_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TryToggleDoubleBar(_project, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out _)) return;
        CommitEdit(EditRefresh.Score | EditRefresh.Palette);
    }

    private void Simile1_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TrySetSimile(_project, Editor.SelectedMeasure, 1,
                !_project.Tracks[TrackMixerGrid.SelectedIndex].Measures[Editor.SelectedMeasure].SimileOneBar)) return;
        CommitEdit(EditRefresh.Score);
    }

    private void Simile2_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        CaptureUndo();
        if (!_arrangementController.TrySetSimile(_project, Editor.SelectedMeasure, 2,
                !_project.Tracks[TrackMixerGrid.SelectedIndex].Measures[Editor.SelectedMeasure].SimileTwoBar)) return;
        CommitEdit(EditRefresh.Score);
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        var bar = CurBar(); if (bar is null) return;
        var txt = GpDialogs.Prompt("Section", "Section name:", bar.SectionName);
        if (txt is null) return;
        CaptureUndo();
        _arrangementController.TrySetSectionName(_project, Editor.SelectedMeasure, txt);
        CommitEdit(EditRefresh.Score | EditRefresh.Arrangement);
    }


    // ---------- note ----------

    private void Dur1_Click(object sender, RoutedEventArgs e) => SetDur(1);
    private void Dur2_Click(object sender, RoutedEventArgs e) => SetDur(2);
    private void Dur4_Click(object sender, RoutedEventArgs e) => SetDur(4);
    private void Dur8_Click(object sender, RoutedEventArgs e) => SetDur(8);
    private void Dur16_Click(object sender, RoutedEventArgs e) => SetDur(16);
    private void Dur32_Click(object sender, RoutedEventArgs e) => SetDur(32);
    private void Dur64_Click(object sender, RoutedEventArgs e) => SetDur(64);
    private void SetDur(int d) { Editor.SetDuration(d); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(d)}"; }
    private void DurLonger_Click(object sender, RoutedEventArgs e) { Editor.Longer(); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(Editor.CurrentDurationDenominator)}"; }
    private void DurShorter_Click(object sender, RoutedEventArgs e) { Editor.Shorter(); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(Editor.CurrentDurationDenominator)}"; }
    private void PitchUp_Click(object sender, RoutedEventArgs e) => Editor.ShiftPitch(1);
    private void PitchDown_Click(object sender, RoutedEventArgs e) => Editor.ShiftPitch(-1);
    private void MoveUpString_Click(object sender, RoutedEventArgs e) => Editor.MoveString(-1);
    private void MoveDownString_Click(object sender, RoutedEventArgs e) => Editor.MoveString(1);
    private void FxArpDown_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ArpeggioDown);
    private void FxArpUp_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ArpeggioUp);

    private void EmptyBar_Click(object sender, RoutedEventArgs e)
    {
        Editor.EmptyBar();
        StatusText.Text = "Bar emptied";
    }
    // The editor captures the undo step itself (EditStarting); capturing here too made two steps.
    private void Dot_Click(object sender, RoutedEventArgs e) { Editor.ToggleDot(); RefreshStatus(); }
    private void DoubleDot_Click(object sender, RoutedEventArgs e) { Editor.SetDots(2); RefreshStatus(); }
    private void Triplet_Click(object sender, RoutedEventArgs e) { Editor.ToggleTriplet(); RefreshStatus(); }
    private void Tie_Click(object sender, RoutedEventArgs e) => Editor.ToggleTie();
    private void Rest_Click(object sender, RoutedEventArgs e) => Editor.ToggleRest();
    private void Fermata_Click(object sender, RoutedEventArgs e) => Editor.ToggleFermata();
    private void Accent_Click(object sender, RoutedEventArgs e) => Editor.CycleAccent();
    private void Staccato_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        CaptureUndo(); c.Staccato = !c.Staccato; CommitEdit(EditRefresh.Score);
    }
    private void Tenuto_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        CaptureUndo(); c.Tenuto = !c.Tenuto; CommitEdit(EditRefresh.Score);
    }

    // ---------- effects ----------

    private void PalmMute_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.PalmMute);
    private void Hopo_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Hopo);
    private void Bend_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Bend);
    private void Slide_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.LegatoSlide);
    private void Vibrato_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Vibrato);
    private void LetRing_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.LetRing);
    private void FxDead_Click(object sender, RoutedEventArgs e) => Editor.ToggleDead();
    private void FxGhost_Click(object sender, RoutedEventArgs e) => Editor.ToggleGhost();
    private void FxShiftSlide_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ShiftSlide);
    private void FxWideVib_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.WideVibrato);
    private void FxTremBar_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.TremoloBar);
    private void FxHarm_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Harmonic);
    private void FxArtHarm_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ArtificialHarmonic);
    private void FxTap_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Tapping);
    private void FxSlap_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Slap);
    private void FxPop_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Pop);
    private void FxTrill_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.Trill);
    private void FxTremPick_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.TremoloPick);
    private void FxFadeIn_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.FadeIn);
    private void FxFadeOut_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.FadeOut);
    private void FxWahOpen_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.WahOpen);
    private void FxWahClose_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.WahClose);
    private void FxBrushDown_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.BrushDown);
    private void FxBrushUp_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.BrushUp);
    private void FxGrace_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        CaptureUndo(); c.IsGrace = !c.IsGrace; CommitEdit(EditRefresh.Score);
    }

    private void Chord_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        var txt = GpDialogs.Prompt("Chord (A)", "Chord name (e.g. Am, G7):", c.ChordName ?? "");
        if (txt is null) return;
        CaptureUndo(); c.ChordName = txt; CommitEdit(EditRefresh.Score);
    }

    private void Text_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        var txt = GpDialogs.Prompt("Text (T)", "Beat text:", c.Text ?? "");
        if (txt is null) return;
        CaptureUndo(); c.Text = txt; CommitEdit(EditRefresh.Score);
    }

    // ---------- markers ----------

    private void AddMarker_Click(object sender, RoutedEventArgs e) => AddSectionAt(Editor.SelectedMeasure);

    /// <summary>Adds a section starting at <paramref name="bar"/> (M, Sections panel, timeline right-click).</summary>
    private void AddSectionAt(int bar)
    {
        bar = Math.Clamp(bar, 0, Math.Max(0, MaxMeasures() - 1));
        if (_project.Markers.FirstOrDefault(m => m.MeasureIndex == bar) is { } existing)
        {
            EditSectionTitle(existing); // one section per bar: edit the one that is already there
            return;
        }
        var marker = GpDialogs.Marker("Section", "#2E74B5");
        if (marker is null) return;
        CaptureUndo();
        _project.Markers.Add(new MarkerModel { MeasureIndex = bar, Title = marker.Value.title, ColorHex = marker.Value.color });
        CommitEdit(EditRefresh.Score | EditRefresh.Markers | EditRefresh.Arrangement);
        StatusText.Text = $"Added section \"{marker.Value.title}\" at bar {bar + 1}";
    }

    private void MarkerGo_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerModel marker) JumpToMarker(marker);
    }

    private void MarkerEdit_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerModel marker) EditSectionTitle(marker);
    }

    private void MarkerList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_restoring || _syncingPlayingSectionSelection) return;
        if (ItemsControl.ContainerFromElement(MarkerList, e.OriginalSource as DependencyObject) is ListBoxItem { DataContext: MarkerModel marker })
        {
            MarkerList.SelectedItem = marker;
            JumpToMarker(marker);
        }
    }

    private void JumpToMarker(MarkerModel marker)
    {
        Editor.SetPosition(Math.Clamp(marker.MeasureIndex, 0, MaxMeasures() - 1), 0, Editor.SelectedString);
        ScrollToCursor();
    }

    private void MarkerDel_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is not MarkerModel m) return;
        CaptureUndo(); _project.Markers.Remove(m); CommitEdit(EditRefresh.Score | EditRefresh.Markers);
    }

    private void PrevSection_Click(object sender, RoutedEventArgs e) => JumpSection(-1);
    private void NextSection_Click(object sender, RoutedEventArgs e) => JumpSection(1);
    private void JumpSection(int dir)
    {
        var marks = _project.Markers.OrderBy(m => m.MeasureIndex).ToList();
        if (marks.Count == 0) { Editor.MoveBar(dir); ScrollToCursor(); return; }
        var cur = Editor.SelectedMeasure;
        MarkerModel? target = dir < 0 ? marks.LastOrDefault(m => m.MeasureIndex < cur) : marks.FirstOrDefault(m => m.MeasureIndex > cur);
        if (target != null) { Editor.SetPosition(target.MeasureIndex, 0, Editor.SelectedString); ScrollToCursor(); }
    }
}
