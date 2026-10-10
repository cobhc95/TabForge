using System.Linq;
using TabForge.Models;
using TabForge.Playback;

namespace TabForge;

/// <summary>
/// Hanging notes (corpus sweep): every note-on must have its note-off after it, in the final event order.
/// Causes fixed: a slide-in or before-the-beat grace note at the very start of the song pushed the previous/own
/// note-off before time zero while its note-on was clamped to zero, and a slide-in after a tremolo-picked note cut
/// the tremolo's note-offs to a time before the attacks that still came after the cut.
/// </summary>
public static partial class SelfTest
{
    // Walks the sorted events the way a synth sees them: an off with nothing sounding, or a note still sounding at the end, is a fault.
    private static string HangingNotes(ScoreTimeline timeline)
    {
        var open = new Dictionary<(int Channel, int Note), int>();
        var faults = new List<string>();
        foreach (var e in timeline.Events)
        {
            var key = (e.Status & 0x0F, e.Data1);
            if (e.IsNoteOn) { open.TryGetValue(key, out var n); open[key] = n + 1; }
            else if (e.IsNoteOff)
            {
                if (open.TryGetValue(key, out var n) && n > 0) open[key] = n - 1;
                else faults.Add($"off without on {e.Data1} at {e.TimeMs:0.0}");
            }
        }
        faults.AddRange(open.Where(p => p.Value > 0).Select(p => $"note {p.Key.Note} still on"));
        return string.Join("; ", faults.Take(4));
    }

    private static void TestNoHangingNotes()
    {
        // Slide-in on the very first beat of the song: its lead would start before time zero.
        var slideFirst = SingleTrack();
        Beat(slideFirst, 0, 0, 0, 4, 60).Notes[0].Techniques.Add("SlideInBelow");
        Beat(slideFirst, 0, 0, 4, 4, 60);
        var slideTimeline = MidiTimelineBuilder.Build(slideFirst, new PlaybackOptions());
        Check("a slide-in on the first beat leaves no hanging note", HangingNotes(slideTimeline).Length == 0, HangingNotes(slideTimeline));
        Check("a slide-in on the first beat attacks no earlier than time zero", slideTimeline.Notes.All(n => n.OnsetMs >= 0), string.Join(",", slideTimeline.Notes.Select(n => n.OnsetMs)));

        // A before-the-beat grace note on the first beat (no room before it): plays on the beat, both notes are released.
        var graceFirst = SingleTrack();
        var first = Beat(graceFirst, 0, 0, 0, 4, 54);
        first.Notes.Add(new TabNote { StringIndex = 0, MidiValue = 54, IsGraceNote = true, GraceBeforeBeat = true, GraceDurationSlots = 0.5 });
        var graceTimeline = MidiTimelineBuilder.Build(graceFirst, new PlaybackOptions());
        Check("a before-the-beat grace note on the first beat leaves no hanging note", HangingNotes(graceTimeline).Length == 0, HangingNotes(graceTimeline));
        var principal = graceTimeline.Notes.First(n => !n.Dead && n.DurationMs > 100);
        Check("a before-the-beat grace note on the first beat takes a slice off the principal note", principal.OnsetMs > 0 && graceTimeline.Notes.All(n => n.OnsetMs >= 0),
            string.Join(",", graceTimeline.Notes.Select(n => n.OnsetMs)));

        // A tremolo-picked note followed by a slide-in note of the next beat: the slide-in cuts the tremolo short.
        var tremolo = SingleTrack();
        var picked = Beat(tremolo, 0, 0, 0, 4, 67);
        picked.Notes[0].Techniques.Add("TremoloPick");
        picked.TremoloPickDenominator = 32;
        Beat(tremolo, 0, 0, 4, 4, 66).Notes[0].Techniques.Add("SlideInAbove");
        // Same pitch again right after: the retrigger gap also cuts the tremolo's last offs.
        Beat(tremolo, 0, 0, 8, 4, 67);
        var tremoloTimeline = MidiTimelineBuilder.Build(tremolo, new PlaybackOptions());
        Check("a tremolo note cut short by a slide-in or a retrigger leaves no hanging note", HangingNotes(tremoloTimeline).Length == 0, HangingNotes(tremoloTimeline));

        // The same with the next note of the same pitch starting inside the tremolo's written length.
        var overlap = SingleTrack();
        var longTremolo = Beat(overlap, 0, 0, 0, 2, 67);
        longTremolo.Notes[0].Techniques.Add("TremoloPick");
        longTremolo.TremoloPickDenominator = 32;
        Beat(overlap, 0, 0, 4, 4, 67);
        var overlapTimeline = MidiTimelineBuilder.Build(overlap, new PlaybackOptions());
        Check("a tremolo note overlapped by the same pitch leaves no hanging note", HangingNotes(overlapTimeline).Length == 0, HangingNotes(overlapTimeline));
        // The cut-away tremolo attacks are not sent at all: no near-silent blip (a sampled plug-in can still voice it,
        // with its release noise), and no note-off of that pitch inside the next note, which would cut it short.
        var pitch67 = overlapTimeline.Events.Where(e => e.Data1 == 67 && (e.IsNoteOn || e.IsNoteOff)).ToList();
        Check("cut-away tremolo attacks send no quiet note", pitch67.Where(e => e.IsNoteOn).All(e => e.Data2 >= 10),
            string.Join(",", pitch67.Where(e => e.IsNoteOn).Select(e => $"{e.TimeMs:0.0}:{e.Data2}")));
        var nextNote = overlapTimeline.Notes.Where(n => n.Midi == 67).OrderBy(n => n.OnsetMs).Last();
        var insideNext = pitch67.Where(e => e.TimeMs > nextNote.OnsetMs + 0.5 && e.TimeMs < nextNote.OnsetMs + nextNote.DurationMs - 0.5).ToList();
        Check("the next note of the same pitch is not cut by a cut-away tremolo attack", insideNext.Count == 0,
            string.Join(",", insideNext.Select(e => $"{e.TimeMs:0.0}:{(e.IsNoteOn ? "on" : "off")}")));

        // A legacy grace mark just after the start (no room for its 10 ms minimum) leaves no hanging note either.
        var legacy = SingleTrack();
        Beat(legacy, 0, 0, 0, 4, 60).Notes[0].Techniques.Add("GraceBefore");
        var legacyTimeline = MidiTimelineBuilder.Build(legacy, new PlaybackOptions());
        Check("a legacy grace mark on the first beat leaves no hanging note", HangingNotes(legacyTimeline).Length == 0, HangingNotes(legacyTimeline));
    }
}
