namespace TabForge.KeyboardMode;

/// <summary>Whether a key the player holds belongs to a note of the song (or the chord the song waits for), is an extra, or is held while nothing is judged (stopped).</summary>
public enum KeyHold : byte { None, Correct, Extra, Held }

/// <summary>The words of a grade flash.</summary>
public enum KeyGradeKind { Perfect, Good, Early, Late, Miss }

/// <summary>A set of MIDI keys (0 to 127) as two words: compared and copied without allocating.</summary>
public struct KeyMask : IEquatable<KeyMask>
{
    public ulong Lo, Hi;
    public void Set(int midi) { if ((uint)midi > 127) return; if (midi < 64) Lo |= 1UL << midi; else Hi |= 1UL << (midi - 64); }
    public readonly bool Has(int midi) => (uint)midi <= 127 && ((midi < 64 ? Lo >> midi : Hi >> (midi - 64)) & 1) != 0;
    public void Clear() { Lo = 0; Hi = 0; }
    public readonly bool Equals(KeyMask other) => Lo == other.Lo && Hi == other.Hi;
    public override readonly bool Equals(object? obj) => obj is KeyMask m && Equals(m);
    public override readonly int GetHashCode() => HashCode.Combine(Lo, Hi);
}

// Owns: what the keyboard view shows about the player: the keys held now (correct or extra) with a version that moves only when it changes, and the latest grade flash (Perfect / Good / Early / Late / Miss)
//   read from the judge's resolved notes, with its fade. A pure state machine: judge results and key events in, state out; the clock is a number the caller passes.
// Does not own: judging (KeyboardModeJudge), the totals (KeyboardModeScore), where events come from (KeyboardModeKeyboardSession) or drawing.
// Tests: TestKeyboardModeKeyView.
public sealed class KeyboardFeedback
{
    /// <summary>A hit within this many milliseconds of the onset reads Good; a hit farther off reads Early or Late.</summary>
    public const double GoodLabelMs = 100;
    /// <summary>The flash is fully visible this long, then fades until <see cref="ShowMs"/>.</summary>
    public const double FadeStartMs = 450, ShowMs = 900;
    /// <summary>A pending note this much later than the first pending one cannot be resolved yet (its onset lies beyond the good window of the song position).</summary>
    private const double ScanReachSec = 0.35;

    private readonly KeyHold[] _held = new KeyHold[128];
    private KeyboardModeJudge? _judge;
    private bool[] _reported = Array.Empty<bool>();
    private int _from;
    private int _heldCount;

    /// <summary>Moves whenever a key is pressed or released (the view redraws its key layer only then).</summary>
    public int HeldVersion { get; private set; }
    /// <summary>Moves when a new grade flash starts.</summary>
    public int GradeVersion { get; private set; }
    /// <summary>The newest flash, or null when none has shown since the last reset.</summary>
    public KeyGradeKind? Latest { get; private set; }
    /// <summary>The key of the newest flash (the note that was hit or missed; the lowest of a chord).</summary>
    public int LatestKey { get; private set; }
    public double LatestAtMs { get; private set; }

    public KeyHold HeldOf(int midi) => (uint)midi < 128 ? _held[midi] : KeyHold.None;

    /// <summary>A key event of the player: a press is Correct when it hit a note (or belongs to the awaited chord), else Extra; a release clears the key.</summary>
    public void Played(KeyboardModePlayed e, bool correct) => Played(e, correct ? KeyHold.Correct : KeyHold.Extra);

    /// <summary>A key event shown as <paramref name="hold"/> while pressed (a release clears the key).</summary>
    public void Played(KeyboardModePlayed e, KeyHold hold)
    {
        if ((uint)e.Midi > 127) return;
        var now = e.On ? hold : KeyHold.None;
        if (_held[e.Midi] == now) return;
        if (_held[e.Midi] == KeyHold.None) _heldCount++; else if (now == KeyHold.None) _heldCount--;
        _held[e.Midi] = now;
        HeldVersion++;
    }

    /// <summary>The run changed or stopped: no key is held and no flash shows.</summary>
    public void Reset()
    {
        if (_heldCount > 0) { Array.Clear(_held); _heldCount = 0; HeldVersion++; }
        _judge = null;
        _from = 0;
        Latest = null;
    }

    /// <summary>Reads the notes the judge resolved since the last call and starts a flash for the worst of them (a miss over a slip over Good over Perfect). True when a flash started.</summary>
    public bool Scan(KeyboardModeJudge judge, double nowMs)
    {
        var results = judge.Results;
        if (!ReferenceEquals(judge, _judge)) { _judge = judge; _reported = new bool[results.Count]; _from = 0; }
        var prefix = judge.ResolvedPrefix;
        var limit = prefix < results.Count ? results[prefix].Expected.OnsetSec + ScanReachSec : double.MaxValue;
        KeyboardModeResult? worst = null;
        var rank = -1;
        for (var i = _from; i < results.Count && results[i].Expected.OnsetSec <= limit; i++)
        {
            if (_reported[i] || !results[i].IsResolved) continue;
            _reported[i] = true;
            var r = Rank(KindOf(results[i]));
            if (r > rank || (r == rank && results[i].Expected.Midi < worst!.Expected.Midi)) { rank = r; worst = results[i]; }
        }
        while (_from < _reported.Length && _reported[_from]) _from++;
        if (worst is null) return false;
        Latest = KindOf(worst);
        LatestKey = worst.Expected.Midi;
        LatestAtMs = nowMs;
        GradeVersion++;
        return true;
    }

    /// <summary>The flash's opacity at <paramref name="nowMs"/>: 1 while fresh, then fading to 0 (0 when none shows).</summary>
    public double Alpha(double nowMs)
    {
        if (Latest is null) return 0;
        var age = nowMs - LatestAtMs;
        return age <= FadeStartMs ? 1 : age >= ShowMs ? 0 : 1 - (age - FadeStartMs) / (ShowMs - FadeStartMs);
    }

    public static KeyGradeKind KindOf(KeyboardModeResult r) =>
        !r.IsHit ? KeyGradeKind.Miss : r.Grade == KeyboardModeGrade.Perfect ? KeyGradeKind.Perfect
        : Math.Abs(r.OffsetMs) <= GoodLabelMs ? KeyGradeKind.Good : r.OffsetMs < 0 ? KeyGradeKind.Early : KeyGradeKind.Late;

    public static string TextOf(KeyGradeKind kind) => kind switch { KeyGradeKind.Perfect => "Perfect", KeyGradeKind.Good => "Good", KeyGradeKind.Early => "Early", KeyGradeKind.Late => "Late", _ => "Miss" };

    private static int Rank(KeyGradeKind kind) => kind switch { KeyGradeKind.Miss => 3, KeyGradeKind.Early or KeyGradeKind.Late => 2, KeyGradeKind.Good => 1, _ => 0 };
}
