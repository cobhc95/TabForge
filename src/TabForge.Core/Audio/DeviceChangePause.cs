namespace TabForge.Audio;

/// <summary>Pauses the songs that are playing while the audio device is changed and resumes them by itself when the new device is ready.</summary>
/// <remarks>
/// Owns: which transports it paused for the current device change, and resuming exactly those.
/// Does not own: the transports (it gets pause / resume actions), the engine configuration, or the fade (the engine's OutputFade).
/// Tests: no dedicated test class.
/// </remarks>
internal sealed class DeviceChangePause
{
    private readonly List<Action> _resume = new();
    private int _pending;   // device changes sent and not answered yet

    /// <summary>True while songs paused here wait for the new device.</summary>
    public bool Waiting => _resume.Count > 0;

    /// <summary>The device is about to change: every transport that is playing (and not paused by the user) is paused; the others are left alone.</summary>
    public void Changing(IEnumerable<(bool Playing, Action Pause, Action Resume)> transports)
    {
        _pending++;
        foreach (var (playing, pause, resume) in transports)
        {
            if (!playing) continue;
            pause();
            _resume.Add(resume);
        }
    }

    /// <summary>
    /// The new device delivers audio: the songs paused by <see cref="Changing"/> continue from where they paused. With several
    /// changes under way, only the answer to the last one resumes (a song never plays through the next teardown).
    /// </summary>
    public void Ready()
    {
        if (_pending > 0 && --_pending > 0) return;
        var resume = _resume.ToArray();
        _resume.Clear();
        foreach (var r in resume) r();
    }

    /// <summary>The device did not open: the songs stay paused at their position (nothing resumes into a closed device) unless another change is still under way.</summary>
    public void Failed() { if (_pending > 0) _pending--; if (_pending == 0) _resume.Clear(); }
}
