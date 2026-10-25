using TabForge.Audio.Contracts;

namespace TabForge.Audio;

// Owns: what the live input strip needs from the engine beyond the shared sync: the tuner's analysis window, and the live input's own meters.
// Does not own: the live input's gain (sent with the track's audio in AudioEngineClient.Sync.cs), the tuner reading (AudioEngineClient.cs).
// Tests: TabForge.Avalonia.Tests.LiveInputStripTests.
public sealed partial class AudioEngineClient
{
    /// <summary>Tells the running engine the tuner's analysis window (ms) once per engine run and value.</summary>
    private void SyncTunerWindow(int ms)
    {
        if (EngineProcessId is not int pid || _tunerWindowSent == (pid, ms)) return;
        _tunerWindowSent = (pid, ms);
        Send(EngineCommand.SetTunerWindow, new SetTunerWindowMessage(ms).Write);
    }

    /// <summary>The live input's raw input level before its chain (0..1+; the strip's Input meter).</summary>
    public float LiveInputPeak => InputPeakOf(LiveInput.Track);

    /// <summary>The live input's level after its chain, volume and pan (0..1+; the strip's Output meter).</summary>
    public float LiveOutputPeak => PeakOf(LiveInput.Track);
}
