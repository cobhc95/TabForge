namespace TabForge.KeyboardMode;

// Owns: the view's time: the playing position made continuous across loop wraps (virtual time), and whether a jump was a seek.
// Does not own: reading the playback position, the loop bounds or the notes.
// Tests: TestKeyboardModeNoteStream.
public sealed class KeyboardModeClock
{
    private const double WrapSlackMs = 600;
    private double _lastReal = double.NaN;
    private int _wraps;
    private KeyboardModeLoop? _loop;

    /// <summary>How many loop passes after the first have started.</summary>
    public int Wraps => _wraps;

    /// <summary>True after <see cref="Update"/> saw a jump that is not a loop wrap (a seek, another loop): the view starts its page again.</summary>
    public bool Jumped { get; private set; }

    /// <summary>Takes the playing position (song time) and the loop in force; returns the virtual time, which never steps back at a wrap.</summary>
    public double Update(double realMs, KeyboardModeLoop? loop)
    {
        Jumped = false;
        KeyboardModeLoop? usable = loop is { IsUsable: true } ? loop : null;
        if (usable != _loop)
        {
            Jumped = true;
            _wraps = 0;
            _loop = usable;
        }
        else if (!double.IsNaN(_lastReal) && realMs < _lastReal - 1)
        {
            if (_loop is { } l && _lastReal >= l.EndMs - WrapSlackMs && realMs <= l.StartMs + WrapSlackMs) _wraps++;
            else { _wraps = 0; Jumped = true; }
        }
        _lastReal = realMs;
        return _wraps > 0 && _loop is { } now ? realMs + _wraps * now.Length : realMs;
    }

    /// <summary>Forgets the position (playback stopped).</summary>
    public void Reset() { _lastReal = double.NaN; _wraps = 0; _loop = null; Jumped = false; }
}
