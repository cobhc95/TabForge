using System;
using System.Collections.Generic;

namespace TabForge.Playback;

// Owns: the note lists an instrument pane lights while a song plays at one song time: the notes sounding now, the notes struck and then
//     released within the hold window (newest first, at most RecentLimit), the last struck note while nothing sounds (a rest), the next
//     movement (the notes sharing the first onset past now) and the later movements up to the preview horizon plus HorizonExtraMovements,
//     one onset group per movement, each upcoming note tagged with its movement depth.
// Does not own: drawing (the panes), the sounding timeline (ScoreTimeline), the score-cursor lights (InstrumentLitNotes).
//     The instrument visualizer takes its sounding, recent, rest and upcoming notes from here.
// Tests: TabForge.Avalonia.Tests.InstrumentPlaybackListsTests.
// Threads: pure, called from the UI thread once per frame. Key: the song time and the note stream. Ceiling: each list holds at most
//     the notes of one movement window, and the onset scratch at most previewHorizon + HorizonExtraMovements onsets. Fill allocates
//     nothing once the snapshot is warm: every list, the onset scratch included, is cleared and refilled in place.

/// <summary>The lit lists for one song time; one instance is reused by its owner.</summary>
public sealed class InstrumentPlaybackSnapshot
{
    public List<NoteEvent> Now { get; } = new();
    public List<NoteEvent> Recent { get; } = new();
    /// <summary>The last struck note, while nothing is sounding (a rest keeps the position readable); empty otherwise.</summary>
    public List<NoteEvent> Released { get; } = new();
    public List<NoteEvent> Next { get; } = new();
    public List<NoteEvent> Upcoming { get; } = new();
    /// <summary>For each entry of Upcoming, its movement depth: 1 for the movement after Next, 2 for the one after that, and so on.</summary>
    public List<int> UpcomingDepth { get; } = new();
    /// <summary>Scratch for Fill: the first onsets of the movements being read. Key: the song position; ceiling: the movement limit; thread: the owner's thread.</summary>
    internal List<double> MovementOnsets { get; } = new();

    public void Clear()
    {
        MovementOnsets.Clear();
        Now.Clear();
        Recent.Clear();
        Released.Clear();
        Next.Clear();
        Upcoming.Clear();
        UpcomingDepth.Clear();
    }

    /// <summary>True when both snapshots hold the same notes in the same lists (the panes repaint only when this changes).</summary>
    public bool SameNotesAs(InstrumentPlaybackSnapshot other) =>
        SameNotes(Now, other.Now) && SameNotes(Recent, other.Recent) && SameNotes(Released, other.Released)
        && SameNotes(Next, other.Next) && SameNotes(Upcoming, other.Upcoming);

    private static bool SameNotes(List<NoteEvent> a, List<NoteEvent> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
            if (!ReferenceEquals(a[i], b[i])) return false;
        return true;
    }
}

public static class InstrumentPlaybackLists
{
    /// <summary>Most recently released notes kept in Recent.</summary>
    public const int RecentLimit = 4;
    /// <summary>Onsets within this many ms of each other are one movement (a chord).</summary>
    public const double OnsetToleranceMs = 0.5;
    /// <summary>Notes older than this many ms are not sounding any more (the scan stops there).</summary>
    public const double SoundingLookbackMs = 6000;
    /// <summary>How long a released note stays in Recent, in ms.</summary>
    public const double DefaultHoldMs = 2600;
    /// <summary>Movements read past the preview horizon, so the lists reach as far.</summary>
    public const int HorizonExtraMovements = 2;
    /// <summary>How many upcoming movements the panes preview when the caller gives no horizon.</summary>
    public const int DefaultPreviewHorizon = 6;

    /// <summary>
    /// Fills <paramref name="target"/> for the song time <paramref name="nowMs"/>. <paramref name="notes"/> is one track's canonical
    /// note stream, sorted by onset (<see cref="NoteTimeline"/>); <paramref name="previewHorizon"/> is how many movements to preview.
    /// </summary>
    public static void Fill(InstrumentPlaybackSnapshot target, NoteEvent[] notes, double nowMs, int previewHorizon, double holdMs = DefaultHoldMs)
    {
        target.Clear();
        var lo = NoteTimeline.FirstIndexAfter(notes, nowMs);
        FillNow(target, notes, lo, nowMs);
        FillRecent(target, notes, lo, nowMs, holdMs);
        if (target.Now.Count == 0 && lo > 0) target.Released.Add(notes[lo - 1]);
        FillMovements(target, notes, lo, Math.Clamp(previewHorizon, 0, 10));
    }

    private static void FillNow(InstrumentPlaybackSnapshot target, NoteEvent[] notes, int lo, double nowMs)
    {
        for (var index = lo - 1; index >= 0; index--)
        {
            var note = notes[index];
            if (note.OnsetMs < nowMs - SoundingLookbackMs) break;
            if (note.EndMs > nowMs) target.Now.Add(note);
        }
    }

    private static void FillRecent(InstrumentPlaybackSnapshot target, NoteEvent[] notes, int lo, double nowMs, double holdMs)
    {
        for (var index = lo - 1; index >= 0 && target.Recent.Count < RecentLimit; index--)
        {
            var note = notes[index];
            if (note.EndMs > nowMs) continue;
            if (nowMs - note.EndMs > holdMs) break;
            target.Recent.Add(note);
        }
    }

    private static void FillMovements(InstrumentPlaybackSnapshot target, NoteEvent[] notes, int lo, int previewHorizon)
    {
        var movementLimit = previewHorizon + HorizonExtraMovements;
        var onsets = target.MovementOnsets;
        for (var i = lo; i < notes.Length && onsets.Count < movementLimit; i++)
        {
            var onset = notes[i].OnsetMs;
            if (onsets.Count == 0 || onset - onsets[^1] > OnsetToleranceMs) onsets.Add(onset);
        }
        if (onsets.Count == 0) return;

        var nextOnset = onsets[0];
        var movement = 0;
        for (var i = lo; i < notes.Length; i++)
        {
            var note = notes[i];
            if (note.OnsetMs - onsets[^1] > OnsetToleranceMs) break;
            while (movement + 1 < onsets.Count && Math.Abs(onsets[movement + 1] - note.OnsetMs) <= OnsetToleranceMs) movement++;
            if (Math.Abs(note.OnsetMs - nextOnset) <= OnsetToleranceMs) target.Next.Add(note);
            else
            {
                target.Upcoming.Add(note);
                target.UpcomingDepth.Add(movement);
            }
        }
    }
}
