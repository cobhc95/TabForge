namespace TabForge.Ports;

// Owns: the narrow contract the instrument choice needs of the sound side: switch a channel's program live, drop that switch again, and
//   audition one short note. The caller (Avalonia transport) implements it over the playback engine; Core never sees the engine.
// Does not own: the engine, the device routing or the document (nothing here edits the song).
// Tests: TabForge.Avalonia.Tests.InstrumentChoiceTests.
// Thread: UI thread only.

/// <summary>What the instrument choice sends to the sound while a picker is open and when it closes.</summary>
public interface IProgramSink
{
    /// <summary>True while the song plays (the next notes use a live program switch; a stopped song also auditions a note).</summary>
    bool IsPlaying { get; }

    /// <summary>Switches the channel to <paramref name="program"/> now; the running schedule and seeks keep it until <see cref="EndPreview"/>.</summary>
    void BeginPreview(int deviceId, int channel, int program);

    /// <summary>Ends the live switch: <paramref name="keep"/> false sends the song's own (restored) program again; true keeps the new sound and brings the running schedule and engine routes in line with the edited song.</summary>
    void EndPreview(int deviceId, int channel, bool keep);

    /// <summary>Plays one note of <paramref name="lengthMs"/> on the channel with the program, without touching the song.</summary>
    void Audition(int deviceId, int channel, int program, int note, int lengthMs);
}
