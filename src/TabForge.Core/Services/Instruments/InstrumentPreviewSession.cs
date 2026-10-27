using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: one instrument-icon picker run on a MIDI track: the opening snapshot, the live preview of each tile click
//   (the canonical apply on the real track, no undo step, no dirty mark) and the two closes — OK commits the choice
//   as exactly one undo step, Cancel restores the opening state bit-identically with no step. The host repaints the
//   header icon on every <see cref="Changed"/>.
// Does not own: the picker window and its tiles (InstrumentPickerWindow), the catalogue (InstrumentCatalog), the apply
//   rules (AudioToInstrumentConversion, TrackSetup), the undo stacks (DocumentEdits, UndoController) or the engine sync (InstrumentChoice and its sink).
// Tests: TabForge.Avalonia.Tests.TrackIconInstrumentPickerTests, InstrumentChoiceTests.
// Thread: UI thread only.
public sealed class InstrumentPreviewSession
{
    /// <summary>The undo label the OK commit stores.</summary>
    public const string CommitLabel = "Change instrument";

    private readonly SongProject _project;
    private readonly TrackModel _track;
    private readonly string _instrumentName;
    private readonly int _program;
    private readonly int _channel;
    private readonly int _capo;
    private readonly TrackKind _kind;
    private readonly List<int> _tunings;
    private readonly List<MeasureModel> _measures;
    private readonly string _rigName;
    private readonly string _rigMap;
    private readonly string _drumPreset;
    private readonly List<DrumMapEntry>? _customMap;
    private readonly bool _remapsToDrums, _remapsToPitched;
    private readonly string _colour;

    /// <summary>Takes one deep snapshot of everything the instrument apply can change (name, program, channel, kind, capo,
    /// strings, rig and articulation map, drum map, notes). The track's display name never changes with the instrument.</summary>
    public InstrumentPreviewSession(SongProject project, TrackModel track)
    {
        _project = project;
        _track = track;
        _instrumentName = track.InstrumentName;
        _program = track.MidiProgram;
        _channel = track.MidiChannel;
        _capo = track.Capo;
        _kind = track.Kind;
        _colour = track.ColorHex;
        _tunings = track.StringTunings.ToList();
        _measures = track.Measures.Select(m => m.Clone()).ToList();
        _rigName = track.Rig.Name;
        _rigMap = track.Rig.ArticulationMap;
        _drumPreset = track.DrumMapPreset;
        _customMap = track.CustomDrumMap?.Select(e => e.Clone()).ToList();
        _remapsToDrums = TrackSetup.ConversionRemapsNotes(track, true);
        _remapsToPitched = TrackSetup.ConversionRemapsNotes(track, false);
    }

    /// <summary>The instrument the track had when the picker opened.</summary>
    public string OriginalName => _instrumentName;

    /// <summary>True when committing <paramref name="chosen"/> moves the track to the other family while it has notes to re-map (they are converted, so the host asks first).</summary>
    public bool RemapsNotes(string chosen) => AudioToInstrumentConversion.IsDrumSound(chosen) ? _remapsToDrums : _remapsToPitched;

    /// <summary>Raised after every preview apply (the host repaints the header icon at once).</summary>
    public event Action? Changed;

    /// <summary>Applies <paramref name="name"/> live to the real track with the canonical apply: no undo step, no dirty mark.
    /// Nothing for an audio track or a null name (its plug-in chain and clips are untouched).</summary>
    public void Preview(string? name)
    {
        if (_track.IsAudio || name is null) return;
        AudioToInstrumentConversion.ApplyInstrument(_project, _track, name);
        Changed?.Invoke();
    }

    /// <summary>Cancels: the track back to its opening state exactly; no undo step, no dirty change, the redo stack untouched.</summary>
    public void Cancel() => Restore();

    /// <summary>Commits <paramref name="chosen"/>: restores the opening state first (so the step's before-state is the original),
    /// then applies the choice once as exactly one undo step. Null or the original: no step.</summary>
    public EditOutcome Commit(IEditTarget document, string? chosen)
    {
        Restore();
        if (chosen is null || string.Equals(chosen, _instrumentName, StringComparison.OrdinalIgnoreCase)) return new EditOutcome(false);
        return document.Run(project => AudioToInstrumentConversion.ApplyInstrument(project, _track, chosen), label: CommitLabel);
    }

    private void Restore()
    {
        _track.InstrumentName = _instrumentName;
        _track.MidiProgram = _program;
        _track.MidiChannel = _channel;
        _track.Capo = _capo;
        _track.Kind = _kind;
        _track.ColorHex = _colour;
        _track.StringTunings = _tunings.ToList();
        _track.Measures = _measures.Select(m => m.Clone()).ToList();
        _track.Rig.Name = _rigName;
        _track.Rig.ArticulationMap = _rigMap;
        _track.DrumMapPreset = _drumPreset;
        _track.CustomDrumMap = _customMap?.Select(e => e.Clone()).ToList();
    }
}
