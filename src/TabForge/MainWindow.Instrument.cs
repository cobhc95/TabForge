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
        var audioSelected = SelectedTrack is { IsAudio: true };
        var track = audioSelected ? null : SelectedTrack;   // an audio track has no instrument: the panel shows its no-track state
        Instrument.DrumLabel = track is { Kind: TrackKind.Drums } drums ? midi => Services.DrumMaps.For(drums, midi).Label : null;
        var state = InstrumentVisualizer.Build(
            _project, track, _timeline, _playheadMs, _isPlayingVisual, _midi.IsPaused,
            InstrumentPane.PreviewHorizon, InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames, InstrumentPane.ScaleHighlight, InstrumentPane.FretboardFrets, options: _options.Visual);
        var editingSelection = InstrumentVisualizer.BuildEditingSelection(
            track, Editor.CurrentCell(), InstrumentPane.LeftHanded, InstrumentPane.ShowNoteNames, InstrumentPane.ScaleHighlight, InstrumentPane.FretboardFrets, options: _options.Visual);
        Instrument.Title = track?.Name ?? "Instrument"; Instrument.AudioTrack = audioSelected;
        InstrumentPane.ApplyInstrumentView(state);
        InstrumentPane.ApplyInstrumentView(editingSelection);
        Instrument.SetState(state);
        Instrument.SetEditingSelection(editingSelection);

        var cell = Editor.CurrentCell();
        var chord = cell?.ChordName;
        PracticeHint.Text = audioSelected ? Services.EditorGuard.Message : track is null
            ? "Select a track"
            : $"{track.Name} · {track.StringTunings.Count} strings · capo {track.Capo}" +
              (string.IsNullOrWhiteSpace(chord) ? "" : $"\nchord: {chord}");
    }

    /// <summary>The TAB line a clicked drum pad writes to (see <see cref="InstrumentPanelController.PercussionPadLine"/>).</summary>
    internal static int PercussionPadLine(TrackModel drumTrack, int percussion) => InstrumentPanelController.PercussionPadLine(drumTrack, percussion);
}
