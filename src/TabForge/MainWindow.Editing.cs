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
public partial class MainWindow : TabForge.Views.Score.IScoreEditHost
{
    // ---------- edits made in the score editor ----------

    /// <summary>The editor's commands change the song here: one undo step, the dirty flag and the timeline invalidation come from <see cref="DocumentEdits.Run"/>.</summary>
    bool TabForge.Views.Score.IScoreEditHost.Run(Func<SongProject, bool> edit, bool invalidatesTimeline) =>
        DocumentEdits.Run(Doc, edit, invalidatesTimeline: invalidatesTimeline).Changed;

    // ---------- undo ----------

    /// <summary>The undo step for a change that follows (a drag, a dialog): <see cref="DocumentEdits.Checkpoint"/>, except while the window is applying a restored state, when the controls it refreshes must not record.</summary>
    private UndoCapture? CheckpointUndo() => _restoring ? null : DocumentEdits.Checkpoint(Doc);

    /// <summary>Undo / redo while playing: the document state (remap, playhead bar) is <see cref="DocumentPlaybackState.RestoreBarMapping"/>; this refreshes the views from it.</summary>
    private void ApplyRestoredPlaybackBarMapping(UndoSnapshot snapshot)
    {
        if (Playback.RestoreBarMapping(snapshot, MaxMeasures(), Playback.Engine.Playhead().Bar) is not { } restored) return;
        Editor.PlaybackBarRemap = restored.Remap;
        if (restored.PlayheadMoved)
        {
            Editor.SetPlayhead(_playheadBar, _playheadCell);
            Arrangement.SetPlayhead(_playheadBar, _playheadFraction, playbackActive: true, playbackPaused: _midi.IsPaused);
            Playhead.SetGeometry(Editor.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.PlaybackDurationGeometries());
        }
        UpdatePlayingSectionMarker(_playheadBar, forceRefresh: true);
        Playback.Engine.RefreshArrangement(_project, restored.Remap, restored.PriorLiveBar);
    }

    /// <summary>The engine compiled a new timeline: the document re-bases its saved mappings; the editor follows.</summary>
    private void RebasePlaybackBarMappings()
    {
        Playback.RebaseBarMappings(MaxMeasures(), () => _undo.Snapshot(_project));
        Editor.PlaybackBarRemap = _playbackBarRemap;
    }


    // The model half (history, restore, clean / dirty, timeline invalidation) is DocumentEdits.Undo / Redo on the displayed document; the view refresh is below.
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undo.CanUndo) return;
        var selected = TrackMixerGrid.SelectedIndex;
        if (DocumentEdits.Undo(Doc) is not { } target) return;
        RefreshAfterRestore(target, selected);
        StatusText.Text = "Undo";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (!_undo.CanRedo) return;
        var selected = TrackMixerGrid.SelectedIndex;
        if (DocumentEdits.Redo(Doc) is not { } target) return;
        RefreshAfterRestore(target, selected);
        StatusText.Text = "Redo";
    }

    private void RefreshAfterRestore(UndoSnapshot snapshot, int selected)
    {
        _restoring = true;
        try
        {
            TempoBox.Text = _project.Tempo.ToString();
            LyricsBox.Text = _project.Lyrics ?? "";
            Editor.Project = _project;
            RefreshTracks();
            if (_project.Tracks.Count > 0) TrackMixerGrid.SelectedIndex = Math.Clamp(selected, 0, _project.Tracks.Count - 1);
            RefreshPluginChain(); RefreshArrangement(); RefreshMarkers(); RefreshInstrument(); RefreshStatus(); UpdateTitle(); UpdateTuningLabel();
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
        var at = Math.Clamp(Editor.SelectedMeasure, 0, MaxMeasures());
        _arrangementController.InsertBar(Doc, at, Editor.SelectedMeasure, moveMarkers: false, fillRests: _settings.Editing.FillBarsWithRests);
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement); StatusText.Text = $"Inserted bar {at + 1}";
    }

    /// <summary>Adds an empty bar after the last one (the standard "Add bar"), keeping the cursor where it is.</summary>
    private void AppendBar()
    {
        var at = MaxMeasures();
        _arrangementController.InsertBar(Doc, at, Math.Max(0, at - 1), moveMarkers: false, fillRests: _settings.Editing.FillBarsWithRests);
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement);
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
        _arrangementController.DeleteBar(Doc, Editor.SelectedMeasure, -1, allTracks: true, moveMarkers: false);
        Editor.SetPosition(Math.Max(0, Editor.SelectedMeasure - 1), 0, Editor.SelectedString);
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
        DocumentEdits.Run(Doc, p =>
        {
            p.Title = r.Title; p.Subtitle = r.Subtitle; p.Artist = r.Artist; p.Album = r.Album;
            p.MusicAuthor = r.MusicAuthor; p.LyricsAuthor = r.LyricsAuthor;
            p.TabAuthor = r.TabAuthor; p.Copyright = r.Copyright;
            p.Instructions = r.Instructions; p.Notice = r.Notice;
            p.Lyrics = r.Lyrics;
            p.GrayInactiveVoice = r.GrayInactiveVoice;
            p.Tempo = r.Tempo;
            if (r.TimeSigNum != p.TimeSignatureNumerator || r.TimeSigDenom != p.TimeSignatureDenominator)
                _arrangementController.SetSongTimeSignature(p, r.TimeSigNum, r.TimeSigDenom);
            if (r.KeySignature != p.KeySignature || r.KeyMinor != p.KeySignatureMinor)
                _arrangementController.SetSongKeySignature(p, r.KeySignature, r.KeyMinor);
            return true;
        });
        LyricsBox.Text = r.Lyrics; TempoBox.Text = r.Tempo.ToString();
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.TimelineGeometry | EditRefresh.Palette | EditRefresh.Status);
        StatusText.Text = "Project settings updated";
    }

    private void TimeSig_Click(object sender, RoutedEventArgs e)
    {
        var current = CurBar();
        var range = SelectedBarRange();
        var r = GpDialogs.TimeSignature(current?.TimeSigNum ?? _project.TimeSignatureNumerator,
            current?.TimeSigDenom ?? _project.TimeSignatureDenominator, range is { } sel ? $"bars {sel.First + 1}-{sel.Last + 1}" : null);
        if (r is null) return;
        var first = range?.First ?? Editor.SelectedMeasure;
        var last = first;
        DocumentEdits.Run(Doc, p =>
        {
            last = range is { } span
                ? BarSignatures.SetTimeRange(p, span.First, span.Last, r.Value.num, r.Value.denom)
                : BarSignatures.SetTime(p, first, r.Value.num, r.Value.denom, !r.Value.onlyThisBar);
            return true;
        });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.TimelineGeometry | EditRefresh.Palette | EditRefresh.Status);
        StatusText.Text = $"Time signature {r.Value.num}/{r.Value.denom} {SignatureSpan(first, last)}";
    }

    /// <summary>The selected bars when the selection spans more than one bar (a signature change then applies to exactly those bars).</summary>
    private (int First, int Last)? SelectedBarRange()
    {
        if (!_selection.HasRange) return null;
        var count = MaxMeasures();
        if (count == 0) return null;
        var (first, last) = (Math.Clamp(Math.Min(_selection.StartBar, _selection.EndBar), 0, count - 1), Math.Clamp(Math.Max(_selection.StartBar, _selection.EndBar), 0, count - 1));
        return last > first ? (first, last) : null;
    }

    /// <summary>"for bar 5", "from bar 5 to bar 9" or "from bar 5 to the end" (the bars a signature change reached).</summary>
    private string SignatureSpan(int first, int last) =>
        last <= first ? $"for bar {first + 1}" : last >= MaxMeasures() - 1 ? $"from bar {first + 1} to the end" : $"from bar {first + 1} to bar {last + 1}";

    private void KeySig_Click(object sender, RoutedEventArgs e)
    {
        var current = CurBar();
        var range = SelectedBarRange();
        var r = GpDialogs.KeySignature(current?.KeySignature ?? _project.KeySignature,
            current?.KeySignatureMinor ?? _project.KeySignatureMinor, range is { } sel ? $"bars {sel.First + 1}-{sel.Last + 1}" : null);
        if (r is null) return;
        var first = range?.First ?? Editor.SelectedMeasure;
        var last = first;
        DocumentEdits.Run(Doc, p =>
        {
            last = range is { } span
                ? BarSignatures.SetKeyRange(p, span.First, span.Last, r.Value.signature, r.Value.minor)
                : BarSignatures.SetKey(p, first, r.Value.signature, r.Value.minor, !r.Value.onlyThisBar);
            return true;
        });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Palette | EditRefresh.Status);
        StatusText.Text = $"Key signature changed {SignatureSpan(first, last)}";
    }

    private void Clef_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        var clef = "";
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryCycleClef(p, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out clef)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score); StatusText.Text = $"Clef {clef}";
    }

    private void TripletFeel_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        var v = false;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryToggleTripletFeel(p, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out v)).Changed) return;
        RefreshAfterEdit(EditRefresh.None); StatusText.Text = v ? "Triplet feel on" : "Triplet feel off";
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
        DocumentEdits.Run(Doc, p => _arrangementController.TrySetDirections(p, Editor.SelectedMeasure, txt, ending));
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Palette);
    }

    private void DoubleBar_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TryToggleDoubleBar(p, TrackMixerGrid.SelectedIndex, Editor.SelectedMeasure, out _)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Palette);
    }

    private void Simile1_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TrySetSimile(p, Editor.SelectedMeasure, 1,
                !p.Tracks[TrackMixerGrid.SelectedIndex].Measures[Editor.SelectedMeasure].SimileOneBar)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score);
    }

    private void Simile2_Click(object sender, RoutedEventArgs e)
    {
        if (CurBar() is null) return;
        if (!DocumentEdits.Run(Doc, p => _arrangementController.TrySetSimile(p, Editor.SelectedMeasure, 2,
                !p.Tracks[TrackMixerGrid.SelectedIndex].Measures[Editor.SelectedMeasure].SimileTwoBar)).Changed) return;
        RefreshAfterEdit(EditRefresh.Score);
    }

    private void Section_Click(object sender, RoutedEventArgs e)
    {
        var bar = CurBar(); if (bar is null) return;
        var txt = GpDialogs.Prompt("Section", "Section name:", bar.SectionName);
        if (txt is null) return;
        DocumentEdits.Run(Doc, p => _arrangementController.TrySetSectionName(p, Editor.SelectedMeasure, txt));
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Arrangement);
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
    private void MoveUpString_Click(object sender, RoutedEventArgs e) => Editor.MoveNotesToAdjacentString(-1);
    private void MoveDownString_Click(object sender, RoutedEventArgs e) => Editor.MoveNotesToAdjacentString(1);
    private void FxArpDown_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ArpeggioDown);
    private void FxArpUp_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique(TechniqueNames.ArpeggioUp);

    private void EmptyBar_Click(object sender, RoutedEventArgs e)
    {
        Editor.EmptyBar();
        StatusText.Text = "Bar emptied";
    }
    // The editor runs its commands through DocumentEdits (one undo step each); nothing is captured here.
    private void Dot_Click(object sender, RoutedEventArgs e) { Editor.ToggleDot(); RefreshStatus(); }
    private void DoubleDot_Click(object sender, RoutedEventArgs e) { Editor.SetDots(2); RefreshStatus(); }
    private void Triplet_Click(object sender, RoutedEventArgs e) { Editor.ToggleTriplet(); RefreshStatus(); }
    private void Tie_Click(object sender, RoutedEventArgs e) => Editor.ToggleTie();
    private void Rest_Click(object sender, RoutedEventArgs e) => Editor.ToggleRest();
    private void Fermata_Click(object sender, RoutedEventArgs e) => Editor.ToggleFermata();
    private void Accent_Click(object sender, RoutedEventArgs e) => Editor.CycleAccent();
    // Staccato and tenuto: the same editor commands as their shortcuts (one undo step).
    private void Staccato_Click(object sender, RoutedEventArgs e) => Editor.ToggleStaccato();
    private void Tenuto_Click(object sender, RoutedEventArgs e) => Editor.ToggleTenuto();

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
    // Same command as the G shortcut (Note.Grace): the per-note GraceBefore technique.
    private void FxGrace_Click(object sender, RoutedEventArgs e) => Editor.ToggleTechnique("GraceBefore");

    private void Chord_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        var txt = GpDialogs.Prompt("Chord name", "Chord name (e.g. Am, G7):", c.ChordName ?? "");
        if (txt is null) return;
        DocumentEdits.Run(Doc, _ => { c.ChordName = txt; return true; }); RefreshAfterEdit(EditRefresh.Score);
    }

    private void Text_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.CurrentCell(); if (c is null) return;
        var txt = GpDialogs.Prompt("Beat text", "Beat text:", c.Text ?? "");
        if (txt is null) return;
        DocumentEdits.Run(Doc, _ => { c.Text = txt; return true; }); RefreshAfterEdit(EditRefresh.Score);
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
        DocumentEdits.Run(Doc, p => { p.Markers.Add(new MarkerModel { MeasureIndex = bar, Title = marker.Value.title, ColorHex = marker.Value.color }); return true; });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Markers | EditRefresh.Arrangement);
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
        DocumentEdits.Run(Doc, p => p.Markers.Remove(m)); RefreshAfterEdit(EditRefresh.Score | EditRefresh.Markers);
        StatusText.Text = $"Removed the section marker '{m.Title}'; its bars and notes stay (Undo brings the marker back)";
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
