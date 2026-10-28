using AlphaTab.Model;

namespace TabForge.Services;

// Owns: the contents of the bars in the older binary formats: voices, beats (duration, tuplet, text, chord name) and tempo changes.
// Does not own: the beat effects and the notes inside a beat (LegacyGpWriter.Notes, LegacyGpWriter.Effects) or the bar headers (LegacyGpWriter.MasterBars).
// Tests: TestLegacyGpExport.
internal sealed partial class LegacyGpWriter
{
    /// <summary>One beat to write: the source beat with the grace notes that lead into it, or a rest added to keep the length of the bar.</summary>
    private sealed record Planned(Beat? Source, List<Beat> Graces, Duration RestDuration);

    /// <summary>Tempo changes inside bars: bar index to (position in the bar 0..1, tempo). The first tempo of the song is in the header.</summary>
    private readonly Dictionary<int, List<(double Ratio, int Tempo)>> _tempos = new();

    private void PlanTempos()
    {
        var running = (int)Math.Round(_score.Tempo);
        for (var b = 0; b < _score.MasterBars.Count; b++)
            foreach (var automation in _score.MasterBars[b].TempoAutomations.OrderBy(a => a.RatioPosition))
            {
                _bar = b;
                var tempo = (int)Math.Round(automation.Value);
                if (automation.IsLinear) Hit("Gradual tempo changes", "written as an instant change where the change ends");
                if (b == 0 && automation.RatioPosition < 1e-6 || tempo == running || tempo is < 1 or > 500) continue;
                if (!_tempos.TryGetValue(b, out var list)) _tempos[b] = list = new();
                list.Add((automation.RatioPosition, tempo));
                running = tempo;
            }
        _bar = -1;
    }

    private void WriteMeasures()
    {
        for (var b = 0; b < _score.MasterBars.Count; b++)
            for (var t = 0; t < _score.Tracks.Count; t++)
            {
                var track = _score.Tracks[t]; var staff = StaffOf(track);
                _track = NameOf(track, t); _bar = b;
                var bar = b < staff.Bars.Count ? staff.Bars[b] : null;
                for (var v = 0; v < (V5 ? 2 : 1); v++)
                    WriteVoice(bar is not null && v < bar.Voices.Count ? bar.Voices[v] : null, track, v, t == 0 && v == 0 ? b : -1, SecondVoiceTracks.Contains(t));
                if (bar is not null && !V5 && bar.Voices.Count > 1 && bar.Voices.Skip(1).Any(x => !x.IsRestOnly && !x.IsEmpty)) Hit("Second voice", $"a {LegacyGpFormats.Extension(_format)} file has one voice per bar; the second voice is left out");
                if (bar is not null && bar.Voices.Count > 2 && bar.Voices.Skip(2).Any(x => !x.IsRestOnly && !x.IsEmpty)) Hit("Third and fourth voice", "left out");
                if (bar is not null && bar.SimileMark != SimileMark.None) Hit("Repeat-bar (simile) marks", "not written; the bar keeps its own notes");
                if (V5) _w.Byte(0);                                                  // line break: none
            }
        _track = "whole song"; _bar = -1;
    }

    private List<Planned> Plan(Voice voice)
    {
        var list = new List<Planned>(); var pending = new List<Beat>();
        foreach (var beat in voice.Beats)
        {
            if (beat.GraceType != GraceType.None) { pending.Add(beat); continue; }
            list.Add(new Planned(beat, pending, Duration.Quarter)); pending = new();
            if (beat.Dots >= 2)
            {
                Hit("Double-dotted notes", "written with one dot, followed by a rest that keeps the length of the bar");
                var rest = (int)beat.Duration * 4;
                if (rest <= 64) list.Add(new Planned(null, new(), (Duration)rest));
            }
        }
        if (pending.Count > 0) Hit("Grace note with no note after it", "left out");
        return list;
    }

    /// <summary>The tracks with notes in a second voice somewhere. Their other bars keep a second voice of rests: the score reader rejects a bar without one after a bar with one.</summary>
    private HashSet<int> SecondVoiceTracks => _secondVoiceTracks ??= _score.Tracks.Select((t, i) => (t, i))
        .Where(x => StaffOf(x.t).Bars.Any(bar => bar.Voices.Count > 1 && !bar.Voices[1].IsRestOnly && !bar.Voices[1].IsEmpty)).Select(x => x.i).ToHashSet();
    private HashSet<int>? _secondVoiceTracks;

    private void WriteVoice(Voice? voice, Track track, int voiceIndex, int tempoBar, bool keepRestVoice)
    {
        if (voice is null || voiceIndex > 0 && (voice.IsRestOnly || voice.IsEmpty) && !(keepRestVoice && voice.Beats.Count > 0)) { _w.Int(0); return; }
        var planned = Plan(voice);
        _w.Int(planned.Count);
        var tempoAt = tempoBar >= 0 ? TempoBeats(planned, tempoBar) : new Dictionary<int, int>();
        for (var i = 0; i < planned.Count; i++)
            WriteBeat(planned[i], track, tempoAt.TryGetValue(i, out var tempo) ? tempo : (int?)null);
    }

    /// <summary>Maps each tempo change of the bar to the beat that starts nearest to it: the older formats put a tempo change on a beat.</summary>
    private Dictionary<int, int> TempoBeats(List<Planned> planned, int bar)
    {
        var result = new Dictionary<int, int>();
        if (!_tempos.TryGetValue(bar, out var changes) || planned.Count == 0) return result;
        var starts = new List<double>(); var at = 0.0;
        foreach (var p in planned) { starts.Add(at); at += Ticks(p.Source?.Duration ?? p.RestDuration, p.Source); }
        var length = Math.Max(1, at);
        foreach (var (ratio, tempo) in changes)
        {
            var best = 0;
            for (var i = 1; i < starts.Count; i++) if (Math.Abs(starts[i] / length - ratio) < Math.Abs(starts[best] / length - ratio)) best = i;
            if (Math.Abs(starts[best] / length - ratio) > 0.02) Hit("Tempo change inside a note", "moved to the nearest beat start");
            result[best] = tempo;
        }
        return result;
    }

    private static double Ticks(Duration duration, Beat? beat)
    {
        var ticks = 3840.0 / Math.Abs((int)duration);
        if (beat is null) return ticks;
        if (beat.Dots >= 1) ticks *= 1.5;
        if (beat.TupletNumerator > 0 && beat.TupletDenominator > 0) ticks *= beat.TupletDenominator / beat.TupletNumerator;
        return ticks;
    }

    private int DurationByte(Duration duration)
    {
        var value = (int)duration;
        if (value is < 1 or > 64) { Hit("Notes shorter than a 64th or longer than a whole", "written as a 64th or a whole note"); value = value < 1 ? 1 : 64; }
        return value switch { 1 => -2, 2 => -1, 4 => 0, 8 => 1, 16 => 2, 32 => 3, _ => 4 };
    }

    /// <summary>The "N in the time of M" code of the older formats (3 for a triplet ...), or 0 when the shape has no code.</summary>
    private int TupletCode(Beat beat)
    {
        var n = (int)beat.TupletNumerator; var d = (int)beat.TupletDenominator;
        if (n == 1 && d == 1 || n <= 0) return 0;
        if (n == 3 && d == 2 || n == 5 && d == 4 || n == 6 && d == 4 || n == 7 && d == 4 || n is >= 9 and <= 13 && d == 8) return n;
        Hit("Tuplets other than 3, 5, 6, 7, 9, 10, 11, 12 and 13", "written as plain notes of their written length");
        return 0;
    }

    private void WriteBeat(Planned p, Track track, int? tempo)
    {
        if (p.Source is null)
        {
            _w.Byte(0x40); _w.Byte(2); _w.Byte(DurationByte(p.RestDuration)); _w.Byte(0);
            if (V5) _w.Short(0);
            return;
        }
        var beat = p.Source; var staff = StaffOf(track);
        var placed = Place(beat, track);
        var rest = beat.Notes.Count == 0;
        var tuplet = TupletCode(beat);
        var name = beat.HasChord ? beat.Chord?.Name : null;
        var fx = PlanEffects(beat, placed);
        var hasText = !string.IsNullOrEmpty(beat.Text);
        var flags = (beat.Dots >= 1 ? 0x01 : 0) | (name is { Length: > 0 } ? 0x02 : 0) | (hasText ? 0x04 : 0) | (fx.Any ? 0x08 : 0) | (tempo is not null ? 0x10 : 0)
            | (tuplet != 0 ? 0x20 : 0) | (rest ? 0x40 : 0);
        _w.Byte(flags);
        if (rest) _w.Byte(2);
        _w.Byte(DurationByte(beat.Duration));
        if (tuplet != 0) _w.Int(tuplet);
        if (name is { Length: > 0 }) WriteChord(name);
        if (hasText) _w.IntByteString(beat.Text);
        if (fx.Any) WriteBeatEffects(fx);
        if (tempo is { } bpm) WriteTempoChange(bpm);
        var stringFlags = 0;
        foreach (var (gs, _) in placed) stringFlags |= 1 << (7 - gs);
        _w.Byte(stringFlags);
        WriteNotes(beat, placed, p.Graces, staff, track, fx);
        if (V5) _w.Short(0);
        if (beat.Ottava != Ottavia.Regular) Hit("Octave marks (8va, 8vb)", "not written; the notes keep their pitch");
        if (beat.WahPedal != WahPedal.None) Hit("Wah pedal marks", "not written");
        if (beat.HasRasgueado) Hit("Rasgueado patterns", "not written");
        if (beat.IsLegatoOrigin) Hit("Legato slurs", "not written; hammer-ons and slides are kept");
    }

    private void WriteChord(string name)
    {
        if (!V4) { _w.Byte(0); _w.IntByteString(name); _w.Int(0); return; }       // the old layout: a name and no diagram
        _w.Byte(1);
        _w.Bool(false); _w.Zeros(3); _w.Byte(0); _w.Byte(0); _w.Byte(0); _w.Int(0); _w.Int(0); _w.Bool(false);   // sharp, root, type, extension, bass, alteration, add
        _w.ByteStringPadded(name, 22); _w.Zeros(3);                                // name, fifth, ninth, eleventh
        _w.Int(1);
        for (var i = 0; i < 7; i++) _w.Int(-1);                                    // no fret on any string: a name only
        _w.Byte(0); _w.Zeros(15); _w.Zeros(7); _w.Byte(0);                         // barres, barre frets, starts and ends, omissions, unused
        for (var i = 0; i < 7; i++) _w.Byte(255);                                  // no fingering
        _w.Bool(false);                                                            // diagram not shown
    }

    /// <summary>A mix-table change that carries only a new tempo (every other value says "no change").</summary>
    private void WriteTempoChange(int tempo)
    {
        _w.Byte(255);                                                              // instrument
        if (V5) { _w.Int(-1); _w.Int(-1); _w.Int(-1); _w.Int(-1); }                // sound engine instrument
        for (var i = 0; i < 6; i++) _w.Byte(255);                                  // volume, balance, chorus, reverb, phaser, tremolo
        if (V5) _w.IntByteString("");                                              // tempo text
        _w.Int(tempo);
        _w.Byte(0);                                                                // the change is instant
        if (_version >= 510) _w.Byte(0);                                           // tempo shown
        if (V4) _w.Byte(0);                                                        // applies to this track only
        if (V5) _w.Byte(255);                                                      // wah
        if (_version >= 510) { _w.IntByteString(""); _w.IntByteString(""); }       // effect name and category
    }
}
