using System.Globalization;

namespace TabForge.AudioEngine.Midi;

/// <summary>Parses and formats note sets such as "36, 38, 40-45, C2, D#3" (note names use C-1 = 0, so C4 = 60).</summary>
public static class NoteSetText
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string NoteName(int n) => $"{Names[((n % 12) + 12) % 12]}{n / 12 - 1}";

    /// <summary>Bit set of notes 0..127 (tokens that do not parse are ignored).</summary>
    public static bool[] Parse(string? text)
    {
        var set = new bool[128];
        if (string.IsNullOrWhiteSpace(text)) return set;
        foreach (var raw in text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var dash = raw.IndexOf('-', 1 < raw.Length ? 1 : 0);
            // "C-1" style names contain a dash; try whole token first.
            if (TryNote(raw, out var single)) { set[single] = true; continue; }
            if (dash > 0 && TryNote(raw[..dash], out var a) && TryNote(raw[(dash + 1)..], out var b))
                for (var n = Math.Min(a, b); n <= Math.Max(a, b); n++) set[n] = true;
        }
        return set;
    }

    public static bool TryNote(string token, out int note)
    {
        note = 0;
        token = token.Trim();
        if (token.Length == 0) return false;
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out note)) return note <= 127;
        var letter = "CDEFGAB".IndexOf(char.ToUpperInvariant(token[0]));
        if (letter < 0) return false;
        var semis = new[] { 0, 2, 4, 5, 7, 9, 11 }[letter];
        var i = 1;
        if (i < token.Length && token[i] == '#') { semis++; i++; }
        else if (i < token.Length && (token[i] == 'b' || token[i] == 'B') && i + 1 < token.Length && (char.IsDigit(token[i + 1]) || token[i + 1] == '-')) { semis--; i++; }
        if (!int.TryParse(token.AsSpan(i), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var oct)) return false;
        note = (oct + 1) * 12 + semis;
        return note is >= 0 and <= 127;
    }

    /// <summary>Compact numeric text ("36,38,40-45") that <see cref="Parse"/> reads back.</summary>
    public static string Format(IEnumerable<int> notes)
    {
        var sorted = notes.Where(n => n is >= 0 and <= 127).Distinct().OrderBy(n => n).ToList();
        var parts = new List<string>();
        for (var i = 0; i < sorted.Count;)
        {
            var j = i;
            while (j + 1 < sorted.Count && sorted[j + 1] == sorted[j] + 1) j++;
            parts.Add(j - i >= 2 ? $"{sorted[i]}-{sorted[j]}" : string.Join(",", sorted.Skip(i).Take(j - i + 1)));
            i = j + 1;
        }
        return string.Join(",", parts);
    }
}

/// <summary>
/// Wraps any note-acting processor with an "applies to" note set. Selected notes go through the processor; the rest pass through
/// unchanged. A note-off follows the decision its note-on made, so pairs never split.
/// </summary>
public sealed class NoteFilteredProcessor : IMidiProcessor
{
    private readonly IMidiProcessor _inner;
    private readonly bool[] _set;
    private readonly bool _only;   // true: only the set; false: all except the set
    private readonly byte[] _decision = new byte[2048];   // 0 none, 1 processed, 2 bypassed
    private readonly MidiBuffer _sel = new();

    public NoteFilteredProcessor(IMidiProcessor inner, int mode, bool[] set) { _inner = inner; _only = mode == 1; _set = set; }

    public static IMidiProcessor Unwrap(IMidiProcessor p) => p is NoteFilteredProcessor f ? f._inner : p;

    private bool Selected(int note) => _set[note & 127] == _only;

    public void Process(MidiBuffer input, MidiBuffer output, in MidiContext ctx)
    {
        _sel.Clear();
        var bypassed = false;
        for (var i = 0; i < input.Count; i++)
        {
            var e = input.Items[i];
            var st = e.Status;
            var kind = st & 0xF0;
            var go = true;
            if (st < 0xF0 && kind is 0x80 or 0x90 or 0xA0)
            {
                var key = ((st & 0x0F) << 7) | (e.Data1 & 0x7F);
                if (kind == 0x90 && e.Data2 > 0) { go = Selected(e.Data1); _decision[key] = (byte)(go ? 1 : 2); }
                else if (_decision[key] != 0) { go = _decision[key] == 1; if (kind != 0xA0) _decision[key] = 0; }
                else go = Selected(e.Data1);
            }
            if (go) _sel.Add(e); else { output.Add(e); bypassed = true; }
        }
        var before = output.Count;
        _inner.Process(_sel, output, ctx);
        if (bypassed && output.Count > before) output.Unsorted = true;
    }

    public void Adopt(IMidiProcessor previous, bool sameParameters)
    {
        if (previous is NoteFilteredProcessor p) Array.Copy(p._decision, _decision, _decision.Length);
        _inner.Adopt(Unwrap(previous), sameParameters);
    }

    public void Reset() { Array.Clear(_decision); _inner.Reset(); }
}
