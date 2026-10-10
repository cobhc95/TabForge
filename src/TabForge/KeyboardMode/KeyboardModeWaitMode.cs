namespace TabForge.KeyboardMode;

/// <summary>What wait mode needs from the transport. The window's implementation runs the same pause and resume sequence as the Play / Pause command.</summary>
internal interface IKeyboardModeWaitHost
{
    /// <summary>True while the song is actually playing (not paused, not stopped).</summary>
    bool IsPlaying { get; }
    void Pause();
    void Resume();
}

// Owns: wait mode of a keyboard run: pauses the song when the next unresolved chord reaches the hit line and resumes it once every note of that chord was pressed (pitch only, no timing limit,
//   any order, over any time; a press made up to PreWindowMs before the pause counts; wrong keys are ignored; a release never undoes a press; a pressed pitch is used up by the chord it
//   satisfied, so a repeated pitch needs a new press); a hand-made skip; the re-arm when the judge changes (loop wrap, seek, hands, track change) or the run stops. Pure state machine: a judge,
//   song seconds and a host.
// Does not own: the pause itself (IKeyboardModeWaitHost), the key queue (KeyboardModeMidiListener), the clock, the setting or any drawing.
// Pause ownership: only a pause this class issued is ever resumed by it. Turning it off, a re-arm or a dispose while waiting resumes the song it paused (the transport is left as it was before the
//   hold); a stopped song has nothing to resume. Turning it on never holds for a chord that is already more than CatchUpSec overdue (a new judge starts at the song position).
// Tests: TestKeyboardModeWaitMode.
internal sealed class KeyboardModeWaitMode
{
    private readonly IKeyboardModeWaitHost _host;
    /// <summary>A press this long before the pause still counts for the chord (the player's hands arrive together, not on the same millisecond).</summary>
    public const long PreWindowMs = 300;
    private const double CatchUpSec = 0.1;
    private readonly Dictionary<int, (long Seq, long Tick)> _latest = new();   // pitch -> its latest press
    private readonly Dictionary<int, long> _used = new();   // pitch -> the press number a chord already took
    private long _seq, _startTick;
    private bool _catchUp;
    private bool _enabled;
    private KeyboardModeJudge? _judge;
    private int _group;
    private int _yielded = -1;   // the group whose wait the player ended by hand (the song was resumed with the transport)
    private bool _waiting;

    public KeyboardModeWaitMode(IKeyboardModeWaitHost host) { _host = host; }

    /// <summary>Milliseconds clock (a self-test replaces it).</summary>
    internal Func<long> Now { get; set; } = () => Environment.TickCount64;

    /// <summary>Off: nothing pauses; a wait in progress is dropped and the song stays paused.</summary>
    public bool Enabled
    {
        get => _enabled;
        set { if (_enabled == value) return; _enabled = value; if (!value) Rearm(); else _catchUp = _judge is not null; }
    }

    /// <summary>True while the song is paused by this class and a chord is awaited.</summary>
    public bool IsWaiting => _waiting;
    /// <summary>How many pauses and resumes this class issued (a self-test counter).</summary>
    public int Pauses { get; private set; }
    public int Resumes { get; private set; }

    /// <summary>A press from the player that the judge did not take (song time may be NaN while paused); only its pitch counts, whenever it comes.</summary>
    public void Press(KeyboardModePlayed e)
    {
        if (e.On) _latest[e.Midi] = (++_seq, Now());
    }

    /// <summary>One frame, after the frame's key events reached the judge. <paramref name="songSec"/> is the playing position, not compensated.</summary>
    public void Step(KeyboardModeJudge judge, double songSec, bool running)
    {
        if (!ReferenceEquals(judge, _judge)) { Rearm(); _judge = judge; _yielded = -1; }
        if (!_enabled) return;
        if (!running) { Drop(); return; }
        if (_waiting)
        {
            if (_host.IsPlaying) { _yielded = _group; _waiting = false; return; }   // the player resumed by hand: do not pause for this chord again
            if (Satisfied(judge)) Finish(judge, resolveNotes: true);
            return;
        }
        while (_group < judge.GroupStarts.Count && (judge.GroupState(_group).Complete || (_catchUp && judge.Results[judge.GroupStarts[_group]].Expected.OnsetSec < songSec - CatchUpSec))) _group++;
        if (_group >= judge.GroupStarts.Count || _group == _yielded || !_host.IsPlaying) return;
        _catchUp = false;
        if (songSec < judge.Results[judge.GroupStarts[_group]].Expected.OnsetSec) return;
        _startTick = Now();
        _waiting = true;
        Pauses++;
        _host.Pause();
    }

    /// <summary>Fills <paramref name="keys"/> with the keys of the awaited chord that are not resolved yet (empty when nothing is awaited).</summary>
    public void AwaitedInto(List<int> keys)
    {
        keys.Clear();
        if (!_waiting || _judge is null) return;
        for (var i = _judge.GroupStarts[_group]; i < GroupEnd(_judge); i++)
            if (!_judge.Results[i].IsResolved) keys.Add(_judge.Results[i].Expected.Midi);
    }

    /// <summary>True while the chord waited for still lacks this key.</summary>
    public bool Awaits(int midi)
    {
        if (!_waiting || _judge is null) return false;
        for (var i = _judge.GroupStarts[_group]; i < GroupEnd(_judge); i++)
            if (!_judge.Results[i].IsResolved && _judge.Results[i].Expected.Midi == midi) return true;
        return false;
    }

    /// <summary>Gives up the awaited chord (its notes are misses) and resumes. False when nothing is awaited.</summary>
    public bool Skip()
    {
        if (!_waiting || _judge is null) return false;
        _judge.MissGroup(_group);
        Finish(_judge, resolveNotes: false);
        return true;
    }

    /// <summary>Forgets the awaited chord and the pointer (a loop wrap, seek, hands or track change, pane hidden, setting off or dispose); a song this class paused plays on.</summary>
    public void Rearm()
    {
        var held = _waiting && !_host.IsPlaying;
        Drop();
        if (held) { Resumes++; _host.Resume(); }
    }

    /// <summary>Forgets the awaited chord and the pointer without touching the transport (the song stopped).</summary>
    public void Drop() { _waiting = false; _group = 0; _judge = null; }

    private int GroupEnd(KeyboardModeJudge judge) => _group + 1 < judge.GroupStarts.Count ? judge.GroupStarts[_group + 1] : judge.Results.Count;

    private bool Satisfied(KeyboardModeJudge judge)
    {
        for (var i = judge.GroupStarts[_group]; i < GroupEnd(judge); i++)
            if (!judge.Results[i].IsResolved && !Pressed(judge.Results[i].Expected.Midi)) return false;
        return true;
    }

    /// <summary>The key was pressed (since the wait began or just before it) and no earlier chord used that press up.</summary>
    private bool Pressed(int midi) =>
        _latest.TryGetValue(midi, out var p) && p.Seq > _used.GetValueOrDefault(midi) && p.Tick >= _startTick - PreWindowMs;

    /// <summary>The chord is done: its pressed notes count as played exactly on their onset (the song clock stood still), then playback resumes once.</summary>
    private void Finish(KeyboardModeJudge judge, bool resolveNotes)
    {
        if (resolveNotes)
            for (var i = judge.GroupStarts[_group]; i < GroupEnd(judge); i++)
                if (!judge.Results[i].IsResolved) judge.Feed(new KeyboardModePlayed(judge.Results[i].Expected.Midi, true, judge.Results[i].Expected.OnsetSec));
        for (var i = judge.GroupStarts[_group]; i < GroupEnd(judge); i++)
            if (_latest.TryGetValue(judge.Results[i].Expected.Midi, out var p)) _used[judge.Results[i].Expected.Midi] = p.Seq;
        _waiting = false;
        _group++;
        Resumes++;
        _host.Resume();
    }
}
