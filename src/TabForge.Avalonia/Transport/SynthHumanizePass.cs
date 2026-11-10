using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;
using TabForge.Sf2Synth;
using TabForge.Sf2Synth.Humanize;

namespace TabForge.Avalonia.Transport;

// Owns: the app's humanize pass over a compiled timeline (PlaybackOptions.Humanizer): with TabForge Synth and "Humanize flat dynamics"
//   on, each sounding track without a VST instrument becomes the written note list the Sf2Synth humanizer reads (performed bar keys,
//   sections from the markers, meter, tempo, strokes and techniques from the score), and its played notes go back into the note-on
//   and note-off events; a channel's controllers and pitch wheel move with the note that sounds when they are sent. The drum channel
//   is humanized too (as DrumProgram): crashes stay under the snare and already-human drums are left alone by Evidence. Other engines
//   and tracks with an instrument never see it.
// Does not own: the model (Sf2Synth Humanize/Humanizer), the compile and the event sort (ScoreToMidiCompiler), the settings rows.
// Tests: HumanizerTests.
public sealed class SynthHumanizePass(PluginSettings settings) : ITimelineHumanizer
{
    public string? Key => SoundEngines.Resolve(settings.SoundEngine) == SoundEngines.TabForgeSynth && settings.SynthHumanize ? FeelOf(settings.SynthFeel).ToString() : null;

    private static Feel FeelOf(string? name) => Enum.TryParse<Feel>(name, true, out var f) ? f : Feel.Natural;

    public void Apply(ScoreTimeline timeline, SongProject project)
    {
        if (Key is null || timeline.Bars.Count == 0) return;
        var o = new SynthOptions { Humanize = true, Feel = FeelOf(settings.SynthFeel) };
        var keys = BarKeys(timeline, project);
        var written = new Dictionary<(int, int, long), NoteEvent>();
        foreach (var n in timeline.Notes) written.TryAdd((n.Channel, n.Midi, (long)Math.Round(n.OnsetMs * 10)), n);
        foreach (var group in timeline.Events.Where(e => e.TrackIndex >= 0 && !e.IsSetup && !e.IsMetronome).GroupBy(e => e.TrackIndex))
            if (group.Key < project.Tracks.Count && project.Tracks[group.Key] is { IsAudio: false } track && !MixerGroups.InstrumentPlays(track))
                Track(group.ToList(), timeline, project, keys, written, o, (uint)track.Id.GetHashCode());
    }

    private static void Track(List<ScoreEvent> ev, ScoreTimeline tl, SongProject p, int[] keys, Dictionary<(int, int, long), NoteEvent> written, SynthOptions o, uint seed)
    {
        var open = new Dictionary<int, Queue<ScoreEvent>>();
        var pairs = new List<(ScoreEvent On, ScoreEvent Off)>();
        foreach (var e in ev)
        {
            if (e.IsArticulation) continue;
            var k = e.Channel * 128 + e.Data1;
            if (e.IsNoteOn) { if (!open.TryGetValue(k, out var q)) open[k] = q = new(); q.Enqueue(e); }
            else if (e.IsNoteOff && open.TryGetValue(k, out var q) && q.Count > 0) pairs.Add((q.Dequeue(), e));
        }
        if (pairs.Count == 0) return;
        var isDrums = pairs[0].On.Channel == ChannelAllocator.PercussionChannel;
        var program = isDrums ? HumanTable.DrumProgram : tl.ChannelSetup.LastOrDefault(e => (e.Status & 0xF0) == 0xC0 && e.Channel == pairs[0].On.Channel)?.Data1 ?? 0;
        var notes = pairs.Select(x => Note(x.On, x.Off, tl, p, keys, written)).ToArray();
        var played = new HumanOut[notes.Length];
        Humanizer.Apply(notes, played, program, o, seed);

        // The channel's other events follow the note sounding when they are sent (bends, slides, keyswitches).
        var byChannel = pairs.Select((x, i) => (x.On.Channel, On: x.On.TimeMs, Off: x.Off.TimeMs, Shift: played[i].OnMs - x.On.TimeMs))
            .GroupBy(x => x.Channel).ToDictionary(g => g.Key, g => g.OrderBy(x => x.On).ToArray());
        foreach (var e in ev.Where(e => !(e.IsNoteOn || e.IsNoteOff) || e.IsArticulation))
        {
            if (!byChannel.TryGetValue(e.Channel, out var list)) continue;
            var j = Array.FindLastIndex(list, x => x.On <= e.TimeMs);
            if (j >= 0 && e.TimeMs <= list[j].Off + 1) e.TimeMs = Math.Max(0, e.TimeMs + list[j].Shift);
        }
        for (var i = 0; i < pairs.Count; i++)
        {
            var (on, off) = pairs[i];
            var floor = on.TimeMs >= tl.PlayFromMs ? tl.PlayFromMs : 0;
            (on.TimeMs, on.Data2, off.TimeMs) = (Math.Max(floor, played[i].OnMs), played[i].Velocity, Math.Max(floor + 1, played[i].OffMs));
        }
    }

    private static HumanNote Note(ScoreEvent on, ScoreEvent off, ScoreTimeline tl, SongProject p, int[] keys, Dictionary<(int, int, long), NoteEvent> written)
    {
        var bi = Math.Max(0, tl.BarIndexAt(on.TimeMs));
        var bar = tl.Bars[bi];
        var m = MusicTime.BarOf(p, bar.Bar);
        int num = m?.TimeSigNum ?? p.TimeSignatureNumerator, den = m?.TimeSigDenom ?? p.TimeSignatureDenominator;
        var beats = Math.Max(1, bar.Slots) / (16.0 / Math.Max(1, den));
        var beat = bar.SlotFraction(on.TimeMs) * beats;
        var section = p.Markers.Where(x => x.MeasureIndex <= bar.Bar).Select(x => x.MeasureIndex).DefaultIfEmpty(0).Max();
        var ne = written.GetValueOrDefault((on.Channel, on.Data1, (long)Math.Round(on.TimeMs * 10)));
        var stroke = ne is null ? -1 - ((long)Math.Round(on.TimeMs * 10) * 16 + on.Channel) : (long)keys[bi] << 32 | (uint)(ne.VoiceIndex << 16 | ne.Cell);
        return new(on.TimeMs, off.TimeMs, on.Data1, on.Data2, ne?.StringIndex ?? -1, keys[bi], keys[bi] - (bar.Bar - section),
            beat, num, den, (bar.EndMs - bar.StartMs) / beats, stroke, ne is null ? Marks.None : MarksOf(ne, p));
    }

    private static Marks MarksOf(NoteEvent ne, SongProject p)
    {
        var m = (ne.Ghost ? Marks.Ghost : 0) | (ne.Dead ? Marks.Dead : 0) | (ne.Technique == "SLIDE" ? Marks.Slide : 0)
            | (ne.Strum > 0 ? Marks.Down : ne.Strum < 0 ? Marks.Up : 0);
        if (ne.TrackIndex >= p.Tracks.Count || ne.Bar >= p.Tracks[ne.TrackIndex].Measures.Count) return m;
        var bar = p.Tracks[ne.TrackIndex].Measures[ne.Bar];
        var cells = ne.VoiceIndex == 0 ? bar.Cells : bar.Voice2Cells;
        if (ne.Cell >= cells.Count) return m;
        var cell = cells[ne.Cell];
        if (cell.Accent > 0) m |= Marks.Accent;
        if (cell.Notes.FirstOrDefault(n => n.StringIndex == ne.StringIndex) is not { } note) return m;
        var t = note.Techniques;
        return m | (TechniqueNames.HasPalmMute(t) ? Marks.PalmMute : 0) | (t.Contains(TechniqueNames.HopoDestination) ? Marks.Legato : 0)
            | (t.Contains("PickUp") ? Marks.Up : 0) | (t.Contains("PickDown") ? Marks.Down : 0);
    }

    // Performed bar keys, so a start mid-song keys every bar as the whole song does: the timeline's bars found in the full performed order.
    private static int[] BarKeys(ScoreTimeline tl, SongProject p)
    {
        var full = PlaybackOrder.Build(p, new PlaybackOptions());
        var src = tl.Bars.Select(b => b.Bar).ToArray();
        var n = Math.Min(4, src.Length);
        var at = Enumerable.Range(0, Math.Max(0, full.Count - n + 1)).Where(i => Enumerable.Range(0, n).All(j => full[i + j] == src[j])).DefaultIfEmpty(-1).First();
        return src.Select((b, k) => at >= 0 ? at + k : b).ToArray();
    }
}
