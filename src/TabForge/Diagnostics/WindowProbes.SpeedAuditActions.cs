using System.IO;
using System.Windows;
using System.Windows.Controls;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;
using TabForge.Views;

namespace TabForge.Diagnostics;

// Owns: the action list of `--speed-audit` (what is timed and how each action is driven and undone).
// Does not own: the timing and the report (WindowProbes.SpeedAudit.cs).
// Tests: none (diagnostics only).
internal sealed partial class WindowProbes
{
    private sealed partial class CaptureRun
    {
        private int _rep;

        private async Task Undo() { Hotkey("Edit.Undo"); await _w.Settle(80); }

        /// <summary>Actions measured both while stopped and while playing.</summary>
        private async Task StoppedAndPlayingAsync(string state)
        {
            _state = state;
            await ClicksAsync();
            await TimeAsync("long audio drop", LongAudioDrop, Undo, reps: 3, note: "valid generated WAV; starts at measured song end and extends every track");
            await DragStartAsync();
        }

        private async Task ClicksAsync()
        {
            var e = _w.Editor;
            // clicks
            await TimeAsync("click bar (score caret)", () => { e.SetBar(4 + _rep++ % 6, seekPlayback: false); return null; });
            await TimeAsync("click note", () => { e.SetPosition(6 + _rep++ % 4, 1, 2, seekPlayback: false); return null; });
            await TimeAsync("click ruler (seek)", () => { e.SetBar(2 + _rep++ % 8, seekPlayback: true); return null; });
            await TimeAsync("click track row", () => { _w.TrackMixerGrid.SelectedIndex = _rep++ % 2 == 0 ? 1 : 0; return null; });
            await TimeAsync("click section (select its bars)", () => { var m = Section(_rep++ % 3); _w._selection.SetRange(0, m.start, m.end, SelectionOrigin.Command); return null; },
                async () => { _w._selection.Clear(SelectionOrigin.Command); await _w.Settle(50); });
            await TimeAsync("click clip (select)", () => { _w.Arrangement.SelectedClip = _rep++ % 2 == 0 ? _longClip : null; return null; });
            await ViewAsync();
        }

        private async Task ViewAsync()
        {
            var e = _w.Editor;
            // scrolling and zoom
            await TimeAsync("score wheel scroll", () => { _w.ScoreScroll.ScrollToVerticalOffset(_w.ScoreScroll.VerticalOffset + (_rep++ % 2 == 0 ? 120 : -120)); return null; });
            await TimeAsync("timeline horizontal scroll", () => { var h = (ScrollViewer)Get(_w.Arrangement, "_horizontal")!; h.ScrollToHorizontalOffset(h.HorizontalOffset + (_rep++ % 2 == 0 ? 200 : -200)); return null; });
            await TimeAsync("timeline vertical scroll", () => { var v = (ScrollViewer)Get(_w.Arrangement, "_controlsScroll")!; v.ScrollToVerticalOffset(_rep++ % 2 == 0 ? 60 : 0); return null; });
            await TimeAsync("score zoom", () => Hotkey(_rep++ % 2 == 0 ? "View.ZoomIn" : "View.ZoomOut"));
            await TimeAsync("timeline zoom (wheel step)", () => { var a = _w.Arrangement; Call(a, "ZoomTimeline", Call(a, "ZoomStep", _rep++ % 2 == 0), 300.0); return null; });
            // resizes
            await TimeAsync("window resize", () => { SetSize(_rep++ % 2 == 0 ? new Size(1500, 950) : new Size(1600, 1000)); return null; });
            await TimeAsync("splitter drag step", SplitterStep);
            await MenusAsync();
        }

        private async Task MenusAsync()
        {
            var e = _w.Editor;
            // menus
            await TimeAsync("context menu: score", () => { _w.ShowScoreContextMenu(new Point(200, 200)); return null; });
            await TimeAsync("context menu: note", () => { _w.ShowNoteContextMenu(new Views.ContextMenuEventArgs(new Point(200, 200)) { Measure = 4, Cell = 0, StringIndex = 2, OverBeat = true, OnNote = true }); return null; });
            await TimeAsync("context menu: timeline bar", () => { _w.ShowArrangementContextMenu(4, 0); return null; });
            await TimeAsync("context menu: timeline range", () => { SelectBars(3, 6); return Call(_w.Window, "BuildSelectionMenu"); }, async () => { _w._selection.Clear(SelectionOrigin.Command); await _w.Settle(50); });
            await TimeAsync("context menu: section", () => Call(_w.Window, "ShowSectionContextMenu", 1, null));
            await TimeAsync("context menu: clip", () => MeasureClipPopup());
            await TimeAsync("context menu: track row", () => MeasureTrackRowPopup());
            // mixer state
            await TimeAsync("mute toggle", () => TrackEdit(TrackEditKind.ToggleMute), async () => { if (_rep++ % 2 == 0) { TrackEdit(TrackEditKind.ToggleMute); await _w.Settle(80); } });
            await TimeAsync("solo toggle", () => TrackEdit(TrackEditKind.ToggleSolo), async () => { TrackEdit(TrackEditKind.ToggleSolo); await _w.Settle(80); });
            await EditsAsync();
        }

        private async Task EditsAsync()
        {
            var e = _w.Editor;
            // editing
            await TimeAsync("note entry (fret digit)", () => { e.SetPosition(8, 0, 2, seekPlayback: false); e.Effects.EnterFret(5, autoAdvance: false); return null; }, Undo);
            await TimeAsync("duration change", () => { e.SetPosition(8, 0, 2, seekPlayback: false); e.Effects.SetDuration(_rep++ % 2 == 0 ? 8 : 4); return null; }, Undo);
        }

        private (int start, int end) Section(int index)
        {
            var markers = _w._project.Markers.OrderBy(m => m.MeasureIndex).ToList();
            if (markers.Count == 0) return (0, 3);
            var i = Math.Min(index, markers.Count - 1);
            var end = i + 1 < markers.Count ? markers[i + 1].MeasureIndex - 1 : Math.Max(markers[i].MeasureIndex, _w.MaxMeasures() - 1);
            return (markers[i].MeasureIndex, end);
        }

        private object? TrackEdit(TrackEditKind kind)
        {
            var handler = (Delegate?)Get(_w.Arrangement, "TrackEditRequested");
            handler?.DynamicInvoke(new TrackEditRequest(1, kind));
            return null;
        }

        private object? SplitterStep()
        {
            var splitter = FindVisuals<System.Windows.Controls.GridSplitter>(_w.Window).FirstOrDefault(s => s.IsVisible && s.Parent is Grid);
            if (splitter is null) return false;
            var grid = (Grid)splitter.Parent;
            var delta = _rep++ % 2 == 0 ? 40 : -40;
            if (splitter.ResizeDirection == GridResizeDirection.Rows || splitter.ActualWidth > splitter.ActualHeight)
            {
                var row = grid.RowDefinitions[Math.Max(0, Grid.GetRow(splitter) - 1)];
                row.Height = new GridLength(Math.Max(40, row.ActualHeight + delta));
            }
            else
            {
                var col = grid.ColumnDefinitions[Math.Max(0, Grid.GetColumn(splitter) - 1)];
                col.Width = new GridLength(Math.Max(40, col.ActualWidth + delta));
            }
            return null;
        }

        private object? Open(string tool) { OpenTool(tool, new()); return null; }
        private async Task Close(string tool) { CloseTool(tool); await _w.Settle(150); }

        /// <summary>Dialogs, structural edits, clips, recording, files and tabs: measured while stopped only.</summary>
        private async Task StoppedOnlyAsync()
        {
            var e = _w.Editor;
            // windows and prompts
            await TimeAsync("open Preferences", () => Open("Preferences"), () => Close("Preferences"), reps: 3);
            await TimeAsync("open Mixer", () => Open("Mixer"), () => Close("Mixer"), reps: 3);
            await TimeAsync("open FX chain", () => Open("FxChain"), () => Close("FxChain"), reps: 3);
            await TimeAsync("open track properties", () => Open("TrackProperties"), () => Close("TrackProperties"), reps: 3);
            await TimeAsync("select bars (range)", () => { SelectBars(5, 6); return null; }, async () => { _w._selection.Clear(SelectionOrigin.Command); await _w.Settle(50); });
            await DeleteBarsPromptAsync();
            await TimeAsync("delete track confirm", () => { Window? shown = null; DialogHost.Capture = d => { shown = d; Offscreen(d); return false; }; Hotkey("Track.Delete"); DialogHost.Capture = _ => false; shown?.Close(); return null; });
            await NoteEditsAsync();
        }

        private async Task NoteEditsAsync()
        {
            var e = _w.Editor;
            // notes
            await TimeAsync("copy note", () => { e.SetPosition(4, 0, 2, seekPlayback: false); return Hotkey("Edit.Copy"); });
            await TimeAsync("paste note", () => { e.SetPosition(9, 0, 2, seekPlayback: false); return Hotkey("Edit.Paste"); }, Undo);
            await TimeAsync("cut note", () => { e.SetPosition(4, 0, 2, seekPlayback: false); return Hotkey("Edit.Cut"); }, Undo);
            await TimeAsync("delete note", () => { e.SetPosition(4, 0, 2, seekPlayback: false); e.Effects.DeleteNote(); return null; }, Undo);
            await TimeAsync("undo", () => Hotkey("Edit.Undo"), async () => { Hotkey("Edit.Redo"); await _w.Settle(80); }, note: "after a delete note");
            await TimeAsync("redo", () => { Hotkey("Edit.Undo"); return Hotkey("Edit.Redo"); }, note: "undo+redo pair");
            await RangeEditsAsync();
        }

        private async Task RangeEditsAsync()
        {
            var e = _w.Editor;
            // bars and ranges
            await TimeAsync("copy bars", () => { SelectBars(3, 4); return Hotkey("Edit.Copy"); });
            await TimeAsync("paste bars", () => { SelectBars(10, 11); return Hotkey("Edit.Paste"); }, Undo);
            await TimeAsync("cut bars", () => { SelectBars(3, 4); return Hotkey("Edit.Cut"); }, Undo);
            await TimeAsync("clear bars (range)", () => { SelectBars(3, 4); return Hotkey("Range.Clear"); }, Undo);
            await TimeAsync("remove bars (range)", () => { SelectBars(3, 4); return Hotkey("Range.Remove"); }, Undo);
            await TimeAsync("insert bar", () => { e.SetBar(5, seekPlayback: false); return Hotkey("Bar.Insert"); }, Undo);
            await TimeAsync("add section", () => { e.SetBar(7, seekPlayback: false); return Hotkey("Section.Add"); }, Undo);
            // sections and areas
            await TimeAsync("section move (drag)", SectionMove, Undo, reps: 3);
            await TimeAsync("area move (drag)", AreaMove, Undo, reps: 3);
            await TrackEditsAsync();
        }

        private async Task TrackEditsAsync()
        {
            var e = _w.Editor;
            // tracks
            await TimeAsync("track duplicate", () => { _w.TrackMixerGrid.SelectedIndex = 0; return Hotkey("TrackRow.Duplicate"); }, Undo);
            await TimeAsync("track add (audio)", () => { _w.Window.AddAudioTrack(); return null; }, Undo);
            await TimeAsync("track delete", () => { _w.TrackMixerGrid.SelectedIndex = 1; DialogHost.Capture = ConfirmYes; var r = Hotkey("Track.Delete"); DialogHost.Capture = _ => false; return r; }, Undo);
            await TimeAsync("track move down", () => { _w.TrackMixerGrid.SelectedIndex = 0; return Hotkey("Track.MoveDown"); }, Undo);
            await TimeAsync("track drag reorder", TrackDrag, Undo, reps: 3);
            await TimeAsync("group copy/paste (track rows)", () => { _w.TrackMixerGrid.SelectedIndex = 0; Hotkey("TrackRow.Copy"); return Hotkey("TrackRow.Paste"); }, Undo);
            await ClipsAsync();
        }

        private async Task ClipsAsync()
        {
            var e = _w.Editor;
            // clips (undo and track edits replace the song's objects: the long clip and its track are looked up again)
            ResolveClip();
            await TimeAsync("clip move", () => { _longClip!.StartSec += _rep++ % 2 == 0 ? 0.5 : -0.5; _w.ClipsChanged(); return null; });
            await TimeAsync("clip fade", () => { ClipSplitGlue.SetFades(_longClip!, _rep++ % 2 == 0 ? 1.5 : 0, 0); _w.ClipsChanged(); return null; });
            await TimeAsync("clip split", () => ClipCmd("Clip.Split"), Undo);
            await TimeAsync("clip split+glue", () => { ClipCmd("Clip.Split"); _w.Arrangement.SelectedClip = _longClip; return ClipCmd("Clip.Glue"); }, Undo, note: "glue after split");
            await TimeAsync("section resize (drag)", SectionResize, Undo, reps: 3);
            await TimeAsync("live take refresh (recording, simulated input)", () => { ResolveClip(); return LiveTakeStep(); }, reps: 10);
            await TimeAsync("record arm toggle", () => { ResolveClip(); var t = _w._project.Tracks[_audioTrack]; t.RecordArm = !t.RecordArm; _w.RefreshTracks(); _w.RefreshArrangement(); return null; });
            await TimeAsync("recording start+stop", RecordStartStop, StopRecordingIfOn, reps: 3, note: "engine capture may be unavailable off-screen; UI part only");
            await FilesAsync();
        }

        private async Task FilesAsync()
        {
            var e = _w.Editor;
            // files and tabs
            await TimeAsync("save (.tforge)", () => { Doc.Path = Path.Combine(_out, "speed-audit-save.tforge"); return Call(_w.Window, "SaveCurrentAsync", Doc, null); }, reps: 3);
            await TimeAsync("open song (new tab)", () => _w.OpenDocumentFromPath(Path.Combine(_out, "speed-audit-save.tforge")), reps: 1);
            _state = "stopped (2 tabs)";
            await TimeAsync("tab switch", () => { Call(_w.Window, "ActivateTabAt", _w._documents.ActiveIndex == 0 ? 1 : 0); return null; });
            _state = "stopped";
        }

        private static void Offscreen(Window d)
        {
            d.ShowInTaskbar = false; d.ShowActivated = false;
            d.WindowStartupLocation = WindowStartupLocation.Manual; d.Left = CaptureOffscreen; d.Top = CaptureOffscreen;
            d.Show(); d.UpdateLayout();
        }

        private static bool? ConfirmYes(Window d)
        {
            d.GetType().GetField("_result", Any)?.SetValue(d, MessageBoxResult.Yes);
            return true;
        }

        private void ResolveClip()
        {
            var project = _w._project;
            foreach (var t in project.Tracks)
                if (t.AudioClips.FirstOrDefault(c => c.Name == "Long clip") is { } clip) { _longClip = clip; _audioTrack = project.Tracks.IndexOf(t); return; }
            AddLongAudioTrack(180);   // an undo in the harness removed the track: it is added again
        }

        private object? SectionResize()
        {
            var tl = _w.Arrangement.TimelineForTest;
            var hits = tl.SectionHits();
            if (hits.Count < 2 || !tl.SimulateSectionResizeStart(0, rightEdge: true)) return false;
            var x0 = hits[0].Bounds.Right;
            for (var k = 1; k <= 4; k++) tl.SimulateSectionResizeMove(x0 - k * 20);
            tl.SimulateSectionDragEnd();
            return null;
        }

        private object? RecordStartStop()
        {
            ResolveClip();
            var t = _w._project.Tracks[_audioTrack];
            t.RecordArm = true;
            _w.RefreshTracks();
            _w.ToggleRecording();
            if (_w.IsRecording) _w.ToggleRecording();
            return null;
        }

        private async Task StopRecordingIfOn()
        {
            if (_w.IsRecording) _w.ToggleRecording();
            if (_audioTrack >= 0 && _audioTrack < _w._project.Tracks.Count) _w._project.Tracks[_audioTrack].RecordArm = false;
            _w.RefreshTracks();
            await _w.Settle(150);
        }

        private object? ClipCmd(string id)
        {
            ResolveClip();
            _w.Arrangement.SelectedClip = _longClip;
            var clips = Get(_w.Window, "_clips")!;
            var cursorSec = (_longClip!.StartSec + 40);
            _w._playheadMs = cursorSec * 1000;
            return Call(clips, "RunHotkey", Doc, id);
        }

        private object? SectionMove()
        {
            var panel = _w.Arrangement;
            var hits = panel.TimelineForTest.SectionHits();
            if (hits.Count < 3) return false;
            var x0 = hits[1].Bounds.X + hits[1].Bounds.Width / 2;
            panel.SimulateSectionDragStart(1, x0);
            for (var k = 1; k <= 6; k++) panel.SimulateSectionDragMove(x0 + (hits[^1].Bounds.Right - x0) * k / 6);
            panel.SimulateSectionDragEnd();
            return null;
        }

        private object? AreaMove()
        {
            SelectBars(3, 4);
            var tl = _w.Arrangement.TimelineForTest;
            _w.Arrangement.BeginAreaMove(2, 3);
            var x0 = (tl.XOfBar(2) + tl.XOfBar(4)) / 2; var x1 = tl.XOfBar(9);
            for (var k = 1; k <= 6; k++) tl.AreaMove.PointerMoved(x0 + (x1 - x0) * k / 6);
            tl.AreaMove.Finish(tl.AreaMove.Target);
            return null;
        }

        private object? TrackDrag()
        {
            var panel = _w.Arrangement;
            var y0 = panel.TrackRowCentreY(0); var y1 = panel.TrackRowCentreY(2) + 4;
            panel.SimulateTrackDragStart(0);
            for (var k = 1; k <= 6; k++) panel.SimulateTrackDragMove(y0 + (y1 - y0) * k / 6);
            panel.SimulateTrackDragEnd();
            return null;
        }

        private LiveTake? _take;
        private object? LiveTakeStep()
        {
            var panel = _w.Arrangement;
            if (_take is null)
            {
                var start = _w.SongClock.BarStartSec(_w._project, 2);
                _take = new LiveTake { Track = _w._project.Tracks[_audioTrack], StartSec = start, EndSec = start };
                panel.LiveTakes.Add(_take);
                _cleanup.Add(() => { panel.LiveTakes.Clear(); panel.RefreshLiveTakes(); });
            }
            _take.EndSec += 2;
            for (var k = 0; k < 40; k++) _take.Peaks.Add(0.5f);
            panel.RefreshLiveTakes();
            return null;
        }
    }
}
