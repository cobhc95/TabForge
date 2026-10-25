using TabForge.Models;
using TabForge.Ports;

namespace TabForge.Services;

// Owns: one instrument choice on a MIDI track, whichever window runs it (the track row icon or the Track properties window): every tile click
//   previews on the track (the model through InstrumentPreviewSession) and on the sound (the channel's program switched live; a stopped song also
//   plays one short note), OK commits as one undo step and keeps the new sound in the running schedule, Cancel restores the track and the sound.
// Does not own: the picker window and its tiles, the catalogue, the apply rules (AudioToInstrumentConversion), the undo stacks, the engine
//   (the IProgramSink the caller passes in; none means a silent choice).
// Tests: TabForge.Avalonia.Tests.InstrumentChoiceTests.
// Thread: UI thread only.
public sealed class InstrumentChoice
{
    /// <summary>The length of the note a stopped song auditions after a tile click, in milliseconds.</summary>
    public const int AuditionMs = 600;

    private const int KitNote = 36, BassNote = 43, GuitarNote = 55, MiddleNote = 60;

    private readonly InstrumentPreviewSession _session;
    private readonly TrackModel _track;
    private readonly IProgramSink? _sink;
    private readonly int _channel;
    private bool _sounded;   // a preview reached the sound since the last end

    public InstrumentChoice(SongProject project, TrackModel track, IProgramSink? sink)
    {
        _session = new InstrumentPreviewSession(project, track);
        _track = track;
        _sink = sink;
        _channel = track.MidiChannel;
    }

    /// <summary>The instrument the track had when the picker opened.</summary>
    public string OriginalName => _session.OriginalName;

    /// <summary>The catalogue entry the picker opens on: by name, else by the track's program and drum channel, so a suffixed name still finds its tile.</summary>
    public InstrumentEntry? Current => InstrumentCatalog.ForTrack(_track.InstrumentName, _track.MidiProgram, _track.IsDrumTrack);

    /// <summary>The picker's note about the track's sound route, or null.</summary>
    public string? RouteNote => InstrumentRouteNote.For(_track);

    /// <summary>True when keeping <paramref name="chosen"/> converts the track's notes to the other family (drums and pitched), so the host confirms before <see cref="Commit"/>.</summary>
    public bool RemapsNotes(string chosen) => _session.RemapsNotes(chosen);

    /// <summary>Raised after every preview (the host repaints the header icon).</summary>
    public event Action? Changed
    {
        add => _session.Changed += value;
        remove => _session.Changed -= value;
    }

    /// <summary>Applies <paramref name="name"/> to the track with no undo step and no dirty mark, and sounds it.</summary>
    public void Preview(string? name)
    {
        if (name is null || _track.IsAudio) return;
        _session.Preview(name);
        if (_sink is null) return;
        _sounded = true;   // the live switch or the audition note changed what the output holds: the end sends the song's own sound again
        // A switch to the other family moves the track to another channel: that channel's real tracks keep their sound, so only a note is heard.
        if (_track.MidiChannel == _channel) _sink.BeginPreview(_track.MidiOutputDeviceId, _channel, _track.MidiProgram);
        if (!_sink.IsPlaying) _sink.Audition(_track.MidiOutputDeviceId, _track.MidiChannel, _track.MidiProgram, NoteFor(_track), AuditionMs);
    }

    /// <summary>OK: the track keeps <paramref name="chosen"/> as one "Change instrument" undo step (with no document the track keeps what was previewed, no step, no second sound); the running
    /// schedule goes on with the new sound. Null or the original: nothing changes and the original sound returns.</summary>
    public EditOutcome Commit(IEditTarget? document, string? chosen)
    {
        EditOutcome outcome;
        if (document is not null) outcome = _session.Commit(document, chosen);
        else outcome = new EditOutcome(chosen is not null && !string.Equals(chosen, OriginalName, StringComparison.OrdinalIgnoreCase));
        End(outcome.Changed);
        return outcome;
    }

    /// <summary>Cancel (and closing the window): the track exactly as it was, and its original sound sent again.</summary>
    public void Cancel()
    {
        _session.Cancel();
        End(keep: false);
    }

    private void End(bool keep)
    {
        if (_sink is null || !(_sounded || keep)) return;
        _sink.EndPreview(_track.MidiOutputDeviceId, _channel, keep);
        _sounded = false;
    }

    /// <summary>A typical pitch for the track's instrument: a kick for a kit, then the bass, guitar or middle register.</summary>
    internal static int NoteFor(TrackModel track) => track.Kind == TrackKind.Drums ? KitNote
        : InstrumentCatalog.Find(track.InstrumentName)?.Category switch { "Bass" => BassNote, "Guitar" => GuitarNote, _ => MiddleNote };
}
