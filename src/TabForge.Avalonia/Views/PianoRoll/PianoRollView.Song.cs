using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: what the piano roll reads from the song besides the notes, refreshed after every reload: the loop region on its clock (bars and sixteenth
//       cells, so a loop can start and end inside a bar) and making one, the section chips of the ruler, the rows (fold and scale highlight),
//       the note colour mode, and the lane under the grid (velocity, or a controller lane on a MIDI track) with its picker.
// Does not own: the loop rules and undo (SelectionLoopLink through the host), the controller storage (PianoRollControllers) or the drawing (PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView
{
    private const string LaneCommand = "PianoRoll.Lane";
    private IReadOnlyList<(double Beat, string Title, UiColor Colour)> _sections = Array.Empty<(double, string, UiColor)>();
    private int _lane = PianoRollControllers.Velocity;

    /// <summary>The loop region on the piano roll's clock, cells as sixteenth slots (-1: to the bar's end), or null.</summary>
    private (double From, double To)? LoopSpan()
    {
        if (_document?.Selection is not { HasLoop: true } selection) return null;
        double? from = null;
        foreach (var bar in Presenter.Map.Bars)
        {
            if (from is null && bar.Bar == selection.LoopStartBar) from = bar.StartBeat + Math.Max(0, selection.LoopStartCell) / SnapCore.SlotsPerBeat;
            if (from is not null && bar.Bar == selection.LoopEndBar)
                return (from.Value, selection.LoopEndCell < 0 ? bar.EndBeat : Math.Min(bar.EndBeat, bar.StartBeat + (selection.LoopEndCell + 1) / SnapCore.SlotsPerBeat));
        }
        return null;
    }

    /// <summary>A ruler drag, a handle drag or a box selection: the loop region becomes <paramref name="from"/> to <paramref name="to"/> (beats), in
    /// written bars and sixteenth cells; whole bars are written as such.</summary>
    private void MakeLoop(double from, double to, bool fromSelection)
    {
        if (_document is null || to <= from) return;
        var count = AudioClipTimelineRules.BarCount(_document.Project);
        if (count == 0) return;
        var first = Presenter.Map.BarAtBeat(from);
        var last = Presenter.Map.BarAtBeat(Math.Max(from, to - 1e-6));
        var startBar = Math.Clamp(first.Bar, 0, count - 1);
        var endBar = Math.Clamp(last.Bar, 0, count - 1);
        var startCell = first.Bar >= count ? 0 : (int)Math.Floor((from - first.StartBeat) * SnapCore.SlotsPerBeat + 1e-6);
        var endSlot = (to - last.StartBeat) * SnapCore.SlotsPerBeat;
        var endSlots = (int)Math.Round(last.Beats * SnapCore.SlotsPerBeat);
        var endCell = last.Bar >= count || endSlot >= endSlots - 1e-6 ? -1 : Math.Max(0, (int)Math.Ceiling(endSlot - 1e-6) - 1);
        if (startBar > endBar) (startBar, endBar, startCell, endCell) = (endBar, startBar, 0, -1);
        _host.MakeLoop(startBar, startCell, endBar, endCell, fromSelection);
        Surface.InvalidateVisual();
    }

    /// <summary>Everything read from the song besides the notes, after each reload.</summary>
    private void RefreshDerived()
    {
        var editing = _host.Editing();
        var scale = PianoRollScale.PitchClasses(editing.ScaleHighlight);
        Surface.ScaleClasses = editing.PianoRollScaleShown ? scale : null;
        Surface.Rows = PianoRollScale.Rows(editing.PianoRollFold, scale, Presenter.Notes);
        Surface.ColourMode = editing.PianoRollColours;
        Surface.LanePicker = !Presenter.IsTab && Presenter.Track is not null;
        if (Presenter.IsTab) _lane = PianoRollControllers.Velocity;
        Surface.ShowLane(_lane, Presenter.Points(_lane));
        Surface.DrawVoice = Presenter.IsTab ? Surface.DrawVoice : 0;
        Surface.NoteNames = Presenter.Track?.PianoRollNoteNames == true;
        Surface.NoteLengthChoice = editing.PianoRollNoteLength;
        Surface.Ghosts = editing.PianoRollGhosts && _document is { } song && Presenter.TrackIndex >= 0 ? PianoRollGhosts.Read(song.Project, Presenter.TrackIndex, Presenter.Map, Presenter.Choice) : Array.Empty<PianoRollNote>();
        Surface.DrumNames = Presenter.Track?.IsDrumTrack == true || Presenter.Track?.MidiChannel == 9;
        _names.IsChecked = Surface.NoteNames;
        _sections = Sections();
    }

    private IReadOnlyList<(double Beat, string Title, UiColor Colour)> Sections()
    {
        if (_document is null) return Array.Empty<(double, string, UiColor)>();
        var fallback = AvaloniaPalette.Get("SectionDefault", ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Light);
        var list = new List<(double, string, UiColor)>();
        foreach (var marker in _document.Project.Markers)
            foreach (var bar in Presenter.Map.Bars)
                if (bar.Bar == marker.MeasureIndex)
                {
                    list.Add((bar.StartBeat, marker.Title, TimelineView.TamedTrackColour(marker.ColorHex, fallback)));
                    break;
                }
        return list;
    }

    /// <summary>Edits another source of the shown track (its tab, or a lane of MIDI takes): the notes, lanes, ghosts and the "Editing:" label follow.</summary>
    internal void UseSource(PianoRollSourceChoice choice)
    {
        Presenter.UseSource(choice);
        RefreshTools();
        RefreshInfo();
        SyncScrollBars();
        Surface.InvalidateVisual();
        Surface.Playhead.InvalidateVisual();
    }

    /// <summary>A click on a MIDI take: the roll edits that take's whole lane (every take on it) and scrolls to it, never the track's tab.</summary>
    public void ShowTakeLane(AudioClip clip)
    {
        UseSource(PianoRollSourceChoice.OnLane(clip.Lane));
        Surface.Reveal(Presenter.Map.BeatsAt(clip.StartSec));   // only when the take's start is off screen
    }

    /// <summary>The recording's live take of this track, drawn translucent over the notes (none: cleared). Cheap and coalesced: it refills a reused list and
    /// repaints the playhead layer alone, never the grid, and does nothing when nothing was drawn and nothing is.</summary>
    internal void ShowLive(TabForge.Avalonia.Features.Recording.Presenter.LiveTakes live)
    {
        var take = Presenter.Track is { } track ? live.Of(track) : null;
        if (take is null || !take.Midi)
        {
            if (Surface.LiveNotes.Count == 0) return;
            Surface.LiveNotes.Clear();
        }
        else PianoRollLiveNotes.Fill(Surface.LiveNotes, Presenter.Map, take.StartSec, take.EndSec, take.Notes, take.Held);
        Surface.Playhead.InvalidateVisual();
    }

    /// <summary>The written bars of the section that holds the bar under <paramref name="beat"/>, or null.</summary>
    private (int Start, int End)? SectionAt(double beat)
    {
        if (_document is null) return null;
        var bar = Presenter.Map.BarAtBeat(beat).Bar;
        foreach (var marker in _document.Project.Markers)
            if (SectionMenuEdits.TryBounds(_document.Project, marker, out var start, out var end) && bar >= start && bar < end) return (start, end - 1);
        return null;
    }

    /// <summary>The lane picker under the lane header: velocity and the controller lanes (radio).</summary>
    private void OpenLaneMenu()
    {
        var rows = PianoRollControllers.Lanes.Select(l => new MenuSpec
        {
            Id = LaneCommand, Arg = l.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Header = l.Name, Checkable = true, Radio = true,
            Checked = l.Id == _lane, NoSetting = "the lane the piano roll shows",
        });
        ContextMenuBuilder.Show(Surface, rows, this);
    }

    /// <summary>The key strip shows note names (or piano keys): saved on the track in the song, as one undo step.</summary>
    internal void SetNoteNames(bool names)
    {
        if (_document is { } document && Presenter.Track is not null && Presenter.Track.PianoRollNoteNames != names)
            TabForge.Documents.DocumentEdits.Run(document, project =>
            {
                project.Tracks[Presenter.TrackIndex].PianoRollNoteNames = names;
                return true;
            }, invalidatesTimeline: false, label: names ? "Show note names" : "Show piano keys");
        Surface.NoteNames = names;
        _names.IsChecked = names;
    }

    /// <summary>Shows lane <paramref name="lane"/> (velocity, a CC number, or 128 for pitch bend) under the grid.</summary>
    internal void ShowLane(int lane)
    {
        _lane = Presenter.IsTab ? PianoRollControllers.Velocity : lane;
        if (!Surface.LaneShown) { Surface.LaneShown = true; SaveLane(); }
        Surface.ShowLane(_lane, Presenter.Points(_lane));
    }
}
