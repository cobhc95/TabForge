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

// MainWindow, fretboard / instrument panel interaction.
public partial class MainWindow
{
    // ---------- instrument visualisation ----------

    private void RefreshInstrument()
    {
        var track = SelectedTrack;
        Instrument.DrumLabel = track is { Kind: TrackKind.Drums } drums ? midi => Services.DrumMaps.For(drums, midi).Label : null;
        var state = InstrumentVisualizer.Build(
            _project, track, _timeline, _playheadMs, _isPlayingVisual, _midi.IsPaused,
            _previewHorizon, _leftHanded, _showNoteNames, _scaleHighlight, _fretboardFrets);
        var editingSelection = InstrumentVisualizer.BuildEditingSelection(
            track, Editor.CurrentCell(), _leftHanded, _showNoteNames, _scaleHighlight, _fretboardFrets);
        Instrument.Title = track?.Name ?? "Instrument";
        ApplyInstrumentView(state);
        ApplyInstrumentView(editingSelection);
        Instrument.SetState(state);
        Instrument.SetEditingSelection(editingSelection);

        var cell = Editor.CurrentCell();
        var chord = cell?.ChordName;
        PracticeHint.Text = track is null
            ? "Select a track"
            : $"{track.Name} · {track.StringTunings.Count} strings · capo {track.Capo}" +
              (string.IsNullOrWhiteSpace(chord) ? "" : $"\nchord: {chord}");
    }

    /// <summary>The TAB line a clicked drum pad writes to: the track's drum map (preset or custom), kept inside the track's lines.</summary>
    internal static int PercussionPadLine(TrackModel drumTrack, int percussion) =>
        Math.Clamp(Services.DrumMaps.For(drumTrack, percussion).TabLine, 0, Math.Max(0, drumTrack.StringTunings.Count - 1));

    /// <summary>Arms a fretboard click/drag gesture; note entry waits until mouse-up so horizontal drags stay edits-free.</summary>
    private void Instrument_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Drum tracks: clicking a sound in the percussion map writes it at the cursor (percussion key map).
        if (SelectedTrack is { } drumTrack && (drumTrack.Kind == TrackKind.Drums || drumTrack.MidiChannel == 9) &&
            Instrument.TryHitPercussion(e.GetPosition(Instrument), out var percussion))
        {
            var line = PercussionPadLine(drumTrack, percussion);
            if (Editor.ToggleFretAtPosition(line, percussion)) Editor.Focus();
            e.Handled = true;
            return;
        }
        if (!Instrument.CanRepositionFretboard) return;
        _instrumentDragArmed = true;
        _instrumentDragging = false;
        _instrumentGestureMoved = false;
        _instrumentDragStart = e.GetPosition(Instrument);
        Instrument.CaptureMouse();
        e.Handled = true;
    }

    private void Instrument_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_instrumentDragArmed || e.LeftButton != MouseButtonState.Pressed) return;
        var pointer = e.GetPosition(Instrument);
        var offsetX = pointer.X - _instrumentDragStart.X;
        var offsetY = pointer.Y - _instrumentDragStart.Y;
        if (Math.Abs(offsetX) >= SystemParameters.MinimumHorizontalDragDistance ||
            Math.Abs(offsetY) >= SystemParameters.MinimumVerticalDragDistance)
            _instrumentGestureMoved = true;

        if (!_instrumentDragging)
        {
            if (Math.Abs(offsetX) < SystemParameters.MinimumHorizontalDragDistance || Math.Abs(offsetX) <= Math.Abs(offsetY)) return;
            _instrumentDragging = true;
            Instrument.Cursor = Cursors.SizeWE;
        }

        var snapPosition = Instrument.NearestPositionForDrag(offsetX);
        Instrument.UpdatePlacementDrag(offsetX, snapPosition);
        e.Handled = true;
    }

    private void Instrument_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_instrumentDragArmed) return;
        var wasDragging = _instrumentDragging;
        var shouldEnterNote = !wasDragging && !_instrumentGestureMoved;
        var point = e.GetPosition(Instrument);
        var finalPosition = Instrument.NearestPositionForDrag(point.X - _instrumentDragStart.X);
        _instrumentDragArmed = false;
        _instrumentDragging = false;
        _instrumentGestureMoved = false;
        Instrument.Cursor = null;

        if (wasDragging)
        {
            Instrument.CommitPlacementDrag(finalPosition);
            _settings.Appearance.FretboardPosition = finalPosition.ToString();
            SaveSettings();
            e.Handled = true;
        }
        else
        {
            Instrument.CancelPlacementDrag();
            if (shouldEnterNote && TryEnterFretAt(point)) e.Handled = true;
        }

        if (Mouse.Captured == Instrument) Mouse.Capture(null);
    }

    private void Instrument_LostMouseCapture(object sender, MouseEventArgs e)
    {
        if (!_instrumentDragArmed) return;
        _instrumentDragArmed = false;
        _instrumentDragging = false;
        _instrumentGestureMoved = false;
        Instrument.Cursor = null;
        Instrument.CancelPlacementDrag();
    }

    private bool TryEnterFretAt(Point point)
    {
        var track = SelectedTrack;
        if (track is null || track.Kind is TrackKind.Drums or TrackKind.Keys) return false;
        if (!Instrument.TryHitFret(point, out var stringIndex, out var fret)) return false;
        if (track.StringTunings.Count == 0 || track.Measures.Count == 0) return false;
        if (!Editor.ToggleFretAtPosition(stringIndex, fret)) return false;
        Editor.Focus();
        return true;
    }
}
