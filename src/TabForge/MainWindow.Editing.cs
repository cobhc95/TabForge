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
// Owns: the window's score editing handlers: undo, bar commands (forwards to BarCommandFlow with its host), notes, effects and markers, with the view refresh after each.
// Does not own: the edit transaction, which is DocumentEdits.Run.
// Tests: listed in docs/feature-map/editing-and-notation.md.
public partial class MainWindow : TabForge.Views.Score.IScoreEditHost
{
    // ---------- edits made in the score editor ----------

    /// <summary>The editor's commands change the song here: one undo step, the dirty flag and the timeline invalidation come from <see cref="DocumentEdits.Run"/>.</summary>
    bool TabForge.Views.Score.IScoreEditHost.Run(Func<SongProject, bool> edit, bool invalidatesTimeline) =>
        DocumentEdits.Run(Doc, edit, invalidatesTimeline: invalidatesTimeline).Changed;

    bool TabForge.Views.Score.IScoreEditHost.Run(Func<SongProject, bool> edit, bool invalidatesTimeline, bool continuesLastStep) =>
        DocumentEdits.Run(Doc, edit, before: continuesLastStep ? Doc.Undo.Latest : null, invalidatesTimeline: invalidatesTimeline).Changed;

    // ---------- undo ----------

    /// <summary>The undo step for a change that follows (a drag, a dialog): <see cref="DocumentEdits.Checkpoint"/>, except while the window is applying a restored state, when the controls it refreshes must not record.</summary>
    private UndoCapture? CheckpointUndo() => _restoring ? null : DocumentEdits.Checkpoint(Doc);

    /// <summary>Undo / redo while playing: the document state (remap, playhead bar) is <see cref="DocumentPlaybackState.RestoreBarMapping"/>; this refreshes the views from it.</summary>
    private void ApplyRestoredPlaybackBarMapping(UndoSnapshot snapshot)
    {
        if (Playback.RestoreBarMapping(snapshot, MaxMeasures(), Playback.Engine.Playhead().Bar) is not { } restored) return;
        Editor.Playback.BarRemap = restored.Remap;
        if (restored.PlayheadMoved)
        {
            Editor.Playback.SetPlayhead(_playheadBar, _playheadCell);
            Arrangement.SetPlayhead(_playheadBar, _playheadFraction, playbackActive: true, playbackPaused: _midi.IsPaused);
            Playhead.SetGeometry(Editor.Playback.PlayheadGeometry());
            Playhead.SetDurationGeometries(Editor.Playback.DurationGeometries());
        }
        UpdatePlayingSectionMarker(_playheadBar, forceRefresh: true);
        Playback.Engine.RefreshArrangement(_project, restored.Remap, restored.PriorLiveBar);
    }

    /// <summary>The engine compiled a new timeline: the document re-bases its saved mappings; the editor follows.</summary>
    private void RebasePlaybackBarMappings()
    {
        Playback.RebaseBarMappings(MaxMeasures(), () => _undo.Snapshot(_project));
        Editor.Playback.BarRemap = _playbackBarRemap;
    }


    // The model half (history, restore, clean / dirty, timeline invalidation) is DocumentEdits.Undo / Redo on the displayed document; the view refresh is below.
    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Effects.UndoWritingMark()) { StatusText.Text = "Undo"; return; }   // "." / triplet on an empty spot is its own step (GP5)
        if (!_undo.CanUndo) return;
        var selected = TrackMixerGrid.SelectedIndex; var cursorBeat = Editor.Effects.CursorBeatForUndo(DocumentEdits.Fingerprint(Doc));
        if (DocumentEdits.Undo(Doc) is not { } target) return;
        RefreshAfterRestore(target, selected, () => Editor.Effects.AfterRestore(cursorBeat));
        StatusText.Text = "Undo";
    }

    private void Redo_Click(object sender, RoutedEventArgs e)
    {
        if (Editor.Effects.RedoWritingMark()) { StatusText.Text = "Redo"; return; }
        if (!_undo.CanRedo) return;
        var selected = TrackMixerGrid.SelectedIndex; var cursorBeat = Editor.Effects.CursorBeat();
        if (DocumentEdits.Redo(Doc) is not { } target) return;
        RefreshAfterRestore(target, selected, () => Editor.Effects.AfterRestore(cursorBeat, target.Fingerprint));
        StatusText.Text = "Redo";
    }

    /// <summary><paramref name="placeCursor"/>: where Undo / Redo leave the cursor (GP5), run once the editor shows the restored song.</summary>
    private void RefreshAfterRestore(UndoSnapshot snapshot, int selected, Action placeCursor)
    {
        _restoring = true; _trackSwitchSync?.Cancel();
        try
        {
            TempoBox.Text = _project.Tempo.ToString();
            LyricsBox.Text = _project.Lyrics ?? "";
            Editor.Project = _project;
            placeCursor();
            Editor.Effects.FollowCursorBeat();
            RefreshTracks();
            if (_project.Tracks.Count > 0) TrackMixerGrid.SelectedIndex = Math.Clamp(selected, 0, _project.Tracks.Count - 1);
            SyncMixerWindows(deferEngineSync: true); RefreshArrangement(); RefreshMarkers(); RefreshInstrument(); RefreshStatus(); UpdateTitle(); UpdateTuningLabel();
            ScheduleFitTimelineToTracks();
            RefreshToolsPalette();
            ApplyRestoredPlaybackBarMapping(snapshot);
            _follow.KeepCursorInSight();
        }
        finally { _restoring = false; }
    }


    // ---------- edit ----------

    private void InsertBeat_Click(object sender, RoutedEventArgs e) { Editor.Effects.InsertBeat(); StatusText.Text = "Inserted beat"; }
    private void DeleteBeats_Click(object sender, RoutedEventArgs e) { Editor.Effects.DeleteBeats(); StatusText.Text = "Deleted beats"; }
    // Same command as the C shortcut (EditCommands.CopyLastBeat through the editor): one behaviour, one undo step.
    private void CopyBeats_Click(object sender, RoutedEventArgs e) => Editor.Effects.CopyLastBeat();

    // ---------- measure ----------

    private BarCommandFlow? _barCommands;
    private BarCommandFlow BarCommands => _barCommands ??= new BarCommandFlow(new BarCommandHost(this), _arrangementController);

    private void InsertBar_Click(object sender, RoutedEventArgs e) => BarCommands.InsertBar();

    /// <summary>Adds an empty bar after the last one (the standard "Add bar"), keeping the cursor where it is.</summary>
    private void AppendBar() => BarCommands.AppendBar();

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

    private void DeleteBar_Click(object sender, RoutedEventArgs e) => BarCommands.DeleteBar();

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

    private void TimeSig_Click(object sender, RoutedEventArgs e) => BarCommands.SetTimeSignature();
    private void KeySig_Click(object sender, RoutedEventArgs e) => BarCommands.SetKeySignature();
    private void Clef_Click(object sender, RoutedEventArgs e) => BarCommands.CycleClef();
    private void TripletFeel_Click(object sender, RoutedEventArgs e) => BarCommands.ToggleTripletFeel();

    // Repeat open/close share EditCommands with the [ and ] shortcuts (through the editor): the same bar change in every
    // track and one undo step. The menu only adds the count prompt when a repeat end is being added.
    private void RepeatOpen_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleRepeatOpen();

    private void RepeatClose_Click(object sender, RoutedEventArgs e)
    {
        var bar = CurBar(); if (bar is null) return;
        if (bar.RepeatEnd) { Editor.Effects.ToggleRepeatClose(); StatusText.Text = "Repeat end removed"; return; }
        var txt = GpDialogs.Prompt("Repeat close", "Repeat times (2-99):", bar.RepeatCount.ToString());
        if (txt is null) return;
        var n = Math.Clamp(int.TryParse(txt, out var v) ? v : 2, 2, TabForge.Playback.PlaybackOrder.MaxRepeats);
        Editor.Effects.ToggleRepeatClose(n);
        StatusText.Text = $"Repeat ×{n}";
    }

    private void Directions_Click(object sender, RoutedEventArgs e) => BarCommands.EditDirections();
    private void DoubleBar_Click(object sender, RoutedEventArgs e) => BarCommands.ToggleDoubleBar();
    private void Simile1_Click(object sender, RoutedEventArgs e) => BarCommands.ToggleSimile(1);
    private void Simile2_Click(object sender, RoutedEventArgs e) => BarCommands.ToggleSimile(2);
    private void Section_Click(object sender, RoutedEventArgs e) => BarCommands.RenameSection();

    private sealed class BarCommandHost : IBarCommandHost
    {
        private readonly MainWindow _window;
        public BarCommandHost(MainWindow window) => _window = window;
        public DocumentSession Document => _window.Doc;
        public EditingSettings Editing => _window._settings.Editing;
        public MeasureModel? CurrentBar => _window.CurBar();
        public int SelectedBar => _window.Editor.SelectedMeasure;
        public int SelectedString => _window.Editor.SelectedString;
        public int SelectedTrackIndex => _window.TrackMixerGrid.SelectedIndex;
        public bool IsSelecting => _window.Editor.IsSelecting;
        public (int Start, int End)? SelectedBars => _window._selection.HasRange ? (_window._selection.StartBar, _window._selection.EndBar) : null;
        public void SetPosition(int bar, int cell, int stringIndex) => _window.Editor.SetPosition(bar, cell, stringIndex);
        public void MoveToBarStart(int bar) => _window.Editor.MoveToBarStart(bar);
        public void WriteLikeBeatBefore(int bar) => _window.Editor.Effects.WriteLikeBeatBefore(bar);
        public void Refresh(EditViews views) => _window.RefreshAfterEdit((EditRefresh)(int)views);
        public void SetStatus(string text) => _window.StatusText.Text = text;
        public (int num, int denom, bool onlyThisBar)? AskTimeSignature(int num, int denom, string? selectedBars) => GpDialogs.TimeSignature(num, denom, selectedBars);
        public (int signature, bool minor, bool onlyThisBar)? AskKeySignature(int signature, bool minor, string? selectedBars) => GpDialogs.KeySignature(signature, minor, selectedBars);
        public string? AskDirections(string current, int ending, out int selectedEnding) => GpDialogs.Directions(current, ending, out selectedEnding);
        public string? AskText(string title, string label, string initial) => GpDialogs.Prompt(title, label, initial);

        public bool ConfirmDeleteBar(int barNumber) => MessageBox.Show(_window, $"Delete bar {barNumber} from every track?",
            "Delete bar", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) == MessageBoxResult.Yes;

        public void RefreshAfterBarDelete()
        {
            _window.RefreshArrangement();
            _window.Editor.InvalidateScoreLayout();
            _window.UpdateTitle();
        }
    }

    // ---------- note ----------

    private void Dur1_Click(object sender, RoutedEventArgs e) => SetDur(1);
    private void Dur2_Click(object sender, RoutedEventArgs e) => SetDur(2);
    private void Dur4_Click(object sender, RoutedEventArgs e) => SetDur(4);
    private void Dur8_Click(object sender, RoutedEventArgs e) => SetDur(8);
    private void Dur16_Click(object sender, RoutedEventArgs e) => SetDur(16);
    private void Dur32_Click(object sender, RoutedEventArgs e) => SetDur(32);
    private void Dur64_Click(object sender, RoutedEventArgs e) => SetDur(64);
    private void SetDur(int d) { Editor.Effects.SetDuration(d); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(d)}"; }
    private void DurLonger_Click(object sender, RoutedEventArgs e) { Editor.Effects.Longer(); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(Editor.CurrentDurationDenominator)}"; }
    private void DurShorter_Click(object sender, RoutedEventArgs e) { Editor.Effects.Shorter(); RefreshStatus(); StatusText.Text = $"Note value: {MusicTime.DurationName(Editor.CurrentDurationDenominator)}"; }
    private void PitchUp_Click(object sender, RoutedEventArgs e) => Editor.Effects.ShiftPitch(1);
    private void PitchDown_Click(object sender, RoutedEventArgs e) => Editor.Effects.ShiftPitch(-1);
    private void MoveUpString_Click(object sender, RoutedEventArgs e) => Editor.Effects.MoveNotesToAdjacentString(-1);
    private void MoveDownString_Click(object sender, RoutedEventArgs e) => Editor.Effects.MoveNotesToAdjacentString(1);
    private void FxArpDown_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.ArpeggioDown);
    private void FxArpUp_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.ArpeggioUp);

    private void EmptyBar_Click(object sender, RoutedEventArgs e)
    {
        Editor.Effects.EmptyBar();
        StatusText.Text = "Bar emptied";
    }
    // The editor runs its commands through DocumentEdits (one undo step each); nothing is captured here.
    private void Dot_Click(object sender, RoutedEventArgs e) { Editor.Effects.ToggleDot(); RefreshStatus(); }
    private void DoubleDot_Click(object sender, RoutedEventArgs e) { Editor.Effects.SetDots(2); RefreshStatus(); }
    private void Triplet_Click(object sender, RoutedEventArgs e) { Editor.Effects.ToggleTriplet(); RefreshStatus(); }
    private void Tie_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTie();
    private void Rest_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleRest();
    private void Fermata_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleFermata();
    private void Accent_Click(object sender, RoutedEventArgs e) => Editor.Effects.CycleAccent();
    // Staccato and tenuto: the same editor commands as their shortcuts (one undo step).
    private void Staccato_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleStaccato();
    private void Tenuto_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTenuto();

    // ---------- effects ----------

    private void PalmMute_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.PalmMute);
    private void Hopo_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Hopo);
    private void Bend_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Bend);
    private void Slide_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.LegatoSlide);
    private void Vibrato_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Vibrato);
    private void LetRing_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.LetRing);
    private void FxDead_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleDead();
    private void FxGhost_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleGhost();
    private void FxShiftSlide_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.ShiftSlide);
    private void FxWideVib_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.WideVibrato);
    private void FxTremBar_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.TremoloBar);
    private void FxHarm_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Harmonic);
    private void FxArtHarm_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.ArtificialHarmonic);
    private void FxTap_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Tapping);
    private void FxSlap_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Slap);
    private void FxPop_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Pop);
    private void FxTrill_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.Trill);
    private void FxTremPick_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.TremoloPick);
    private void FxFadeIn_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.FadeIn);
    private void FxFadeOut_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.FadeOut);
    private void FxWahOpen_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.WahOpen);
    private void FxWahClose_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.WahClose);
    private void FxBrushDown_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.BrushDown);
    private void FxBrushUp_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique(TechniqueNames.BrushUp);
    // Same command as the G shortcut (Note.Grace): the per-note GraceBefore technique.
    private void FxGrace_Click(object sender, RoutedEventArgs e) => Editor.Effects.ToggleTechnique("GraceBefore");

    private void Chord_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.Effects.CurrentCell(); if (c is null) return;
        var txt = GpDialogs.Prompt("Chord name", "Chord name (e.g. Am, G7):", c.ChordName ?? "");
        if (txt is null) return;
        DocumentEdits.Run(Doc, _ => { c.ChordName = txt; return true; }); RefreshAfterEdit(EditRefresh.Score);
    }

    private void Text_Click(object sender, RoutedEventArgs e)
    {
        var c = Editor.Effects.CurrentCell(); if (c is null) return;
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
        var marker = GpDialogs.Marker("Section", "#2E74B5", bar: bar);
        if (marker is null) return;
        DocumentEdits.Run(Doc, p => { p.Markers.Add(new MarkerModel { MeasureIndex = bar, Title = marker.Value.title, ColorHex = marker.Value.color }); return true; });
        RefreshAfterEdit(EditRefresh.Score | EditRefresh.Markers | EditRefresh.Arrangement);
        StatusText.Text = $"Added section \"{marker.Value.title}\" at bar {bar + 1}";
    }

    private void MarkerEdit_Click(object sender, RoutedEventArgs e)
    {
        if (MarkerList.SelectedItem is MarkerModel marker) EditSectionTitle(marker);
    }

    private void MarkerList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || MarkerList.SelectedItem is not MarkerModel marker) return;
        JumpToMarker(marker);
        e.Handled = true;
    }

    private void MarkerList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_restoring || _gestures.SyncingPlayingSelection) return;
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
