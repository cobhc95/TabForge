using TabForge.Models;
using TabForge.Playback;

namespace TabForge.KeyboardMode;

// Owns: the fixed made-up play-along state of the Keyboard mode captures: a two-hand riff written over four bars of one track (in memory), the note source of that track, and a judge, score and
//   feedback as if the song had been played a little off the beat up to a moment of a bar (one note missed, the last note's grade flashing, the notes sounding now held right, one extra key held).
// Does not own: drawing (the WPF KeyboardModeView, the Avalonia KeyboardStripView), the real run (KeyboardModeKeyboardSession) or the capture steps that call this.
// Tests: TestKeyboardModeKeyView (WPF capture "learn-playing" with "keys"), TabForge.Avalonia.Tests.KeyboardStripViewTests.
public sealed class KeyboardPlayAlongStub
{
    /// <summary>How long the last grade flash stays fully visible (an hour: it never fades during a capture).</summary>
    private const long FlashHoldMs = 3_600_000;

    private KeyboardPlayAlongStub(KeyboardNoteSource source, KeyboardModeJudge judge, KeyboardModeScore score, KeyboardFeedback feedback, double nowMs, ScoreTimeline timeline)
    {
        Source = source; Judge = judge; Score = score; Feedback = feedback; NowMs = nowMs; Timeline = timeline;
    }

    public KeyboardNoteSource Source { get; }
    public KeyboardModeJudge Judge { get; }
    public KeyboardModeScore Score { get; }
    public KeyboardFeedback Feedback { get; }
    /// <summary>The song time of the shown moment, in milliseconds.</summary>
    public double NowMs { get; }
    public ScoreTimeline Timeline { get; }

    /// <summary>Writes the riff over four bars from <paramref name="bar"/> of track <paramref name="trackIndex"/> (it becomes a keyboard track) and builds the state at <paramref name="fraction"/> of that bar; <paramref name="heldKeys"/> false leaves the player's keys up (the wait-mode capture).</summary>
    public static KeyboardPlayAlongStub Build(SongProject project, int trackIndex, int bar, double fraction, bool heldKeys = true)
    {
        var track = project.Tracks[trackIndex];
        track.Kind = TrackKind.Keys;
        WriteRiff(track, bar);
        var timeline = MidiTimelineBuilder.Build(project, new PlaybackOptions());
        var span = timeline.Bars[Math.Clamp(bar, 0, timeline.Bars.Count - 1)];
        var ms = span.StartMs + fraction * (span.EndMs - span.StartMs);
        var source = KeyboardNoteSource.Build(timeline, project, trackIndex);
        var expected = source.Expected();
        var judge = new KeyboardModeJudge(expected);
        var feedback = new KeyboardFeedback();
        var score = new KeyboardModeScore();
        var now = ms / 1000;
        // Notes before now were played a little off the beat, one was missed; the last of them gives the grade flash.
        var past = expected.Where(e => e.OnsetSec < now - 0.25).ToList();
        for (var i = 0; i < past.Count; i++)
        {
            if (i == past.Count - 1) { judge.Advance(past[i].OnsetSec); score.Absorb(judge); feedback.Scan(judge, 0); }
            if (i % 7 == 5 && i < past.Count - 1) continue;
            var offset = (i % 3 - 1) * 0.03 + (i == past.Count - 1 ? 0.07 : 0);
            judge.Feed(new KeyboardModePlayed(past[i].Midi, true, past[i].OnsetSec + offset));
        }
        judge.Advance(now - 0.25);
        score.Absorb(judge);
        feedback.Scan(judge, Environment.TickCount64 + FlashHoldMs);
        if (heldKeys)
        {
            foreach (var e in expected.Where(e => e.OnsetSec <= now && now < e.OnsetSec + Math.Max(0.1, e.DurationSec)))
                feedback.Played(new KeyboardModePlayed(e.Midi, true, e.OnsetSec), true);
            feedback.Played(new KeyboardModePlayed(source.Highest - 14, true, now), false);   // a key that matches nothing
        }
        return new KeyboardPlayAlongStub(source, judge, score, feedback, ms, timeline);
    }

    /// <summary>Four bars with every length: quarter and half chords of both hands, a whole-bar chord, eighths and a sixteenth run, and a half note tied across the bar line into a whole-bar bar.</summary>
    public static void WriteRiff(TrackModel track, int bar)
    {
        // (cell, denominator, tied, midis): a cell has one length, so a chord of both hands shares it. The first midi is on string 0 so a tie finds its origin.
        (int Cell, int Den, bool Tied, int[] Midis)[][] rhythm =
        {
            new[] { (0, 4, false, new[] { 72, 48, 52 }), (4, 8, false, new[] { 76 }), (6, 8, false, new[] { 79 }), (8, 2, false, new[] { 83, 55, 59, 86 }) },
            new[] { (0, 1, false, new[] { 74, 43, 47, 50, 77 }) },
            new[] { (0, 16, false, new[] { 72 }), (1, 16, false, new[] { 74 }), (2, 16, false, new[] { 76 }), (3, 16, false, new[] { 77 }), (4, 8, false, new[] { 81, 45 }), (6, 8, false, new[] { 77 }), (8, 2, false, new[] { 79, 45, 48 }) },
            new[] { (0, 2, true, new[] { 79 }), (8, 4, false, new[] { 72, 36, 43, 76 }), (12, 4, false, new[] { 74, 41 }) },
        };
        for (var b = 0; b < 4 && bar + b < track.Measures.Count; b++)
        {
            var cells = track.Measures[bar + b].Cells;
            foreach (var c in cells) { c.Notes.Clear(); c.IsRest = false; c.IsTied = false; }
            foreach (var (cell, den, tied, midis) in rhythm[b])
            {
                cells[cell].DurationDenominator = den; cells[cell].IsTied = tied;
                for (var i = 0; i < midis.Length; i++)
                {
                    // A plausible fingering (thumb on the inner note of each hand), so the finger numbers show: 1 thumb to 5 little, stored as 0 to 4.
                    var right = midis[i] >= KeyboardHands.MiddleC;
                    var rank = midis.Count(m => (m >= KeyboardHands.MiddleC) == right && (right ? m < midis[i] : m > midis[i]));
                    cells[cell].Notes.Add(new TabNote { StringIndex = i, Fret = 0, MidiValue = midis[i], LeftHandFinger = Math.Min(4, rank * 2) });
                }
            }
        }
    }
}
