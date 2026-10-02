using System.Globalization;
using System.Windows;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;
using TabForge.Visualization;
using RenderDraw = TabForge.Visualization.Draw;
using TabForge.Views.Score;
using static TabForge.Views.StaffNotationGeometry;

namespace TabForge.Views;

/// <summary>Builds the ties, hammer-on/pull-off slurs and slide strokes of a measure layout.</summary>
internal static class StaffNotationArcs
{
    /// <summary>A simile bar shows only its sign: no tie runs into it or out of it (the notes behind it are a copy kept for playback).</summary>
    internal static bool IsSimileBar(TrackModel? track, int measureIndex) =>
        track is not null && measureIndex >= 0 && measureIndex < track.Measures.Count && (track.Measures[measureIndex].SimileOneBar || track.Measures[measureIndex].SimileTwoBar);

    internal static List<StaffNotationTie> BuildTies(TrackModel? track, int measureIndex, int measureSlots,
        IReadOnlyList<StaffNotationBeat> beats, int voiceIndex, double staffTop)
    {
        var ties = new List<StaffNotationTie>();
        foreach (var beat in beats.Where(b => b.Notes.Count > 0))
        foreach (var note in beat.Notes)
        {
            if (note.Source.Tied || beat.Cell.IsTied)
            {
                var previous = PreviousNoteInMeasure(beats, beat, note.Source, track);
                if (previous is { } prior)
                {
                    var (start, end) = ArcInsets(prior.Beat, prior.Note, beat, note, staffTop);
                    ties.Add(new StaffNotationTie(prior.Note.X, prior.Note.Y, note.X, note.Y,
                        Above: !beat.StemUp, IsStub: false, TowardLeft: false, StartInset: start, EndInset: end));
                }
                else if (beat.StartSlots < PositionEpsilon && !IsSimileBar(track, measureIndex - 1) &&
                          FindAdjacentBarNote(track, measureIndex - 1, note.Source, track, voiceIndex) is { } barPrior)
                {
                    // The stub reaches left over the chord's accidentals and ghost bracket: start it beyond them.
                    var stubX = note.X;
                    var room = GhostRoom(staffTop, beat);
                    var leftmost = beat.Notes.Min(n => n.X);
                    foreach (var other in beat.Notes)
                    {
                        if (other.Accidental is null || Math.Abs(other.Y - note.Y) > 14) continue;
                        stubX = Math.Min(stubX, leftmost - HeadRadiusX - 6.5 - room - other.AccidentalColumn * 10 - 4.5 + 4);
                    }
                    if (note.Source.Ghost) stubX = Math.Min(stubX, note.X - HeadRadiusX - GhostOpenGap - 1.5 - (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) + 4);
                    ties.Add(new StaffNotationTie(stubX, note.Y, stubX, note.Y,
                        Above: !beat.StemUp, IsStub: true, TowardLeft: true));
                }
            }

            if (track is null || measureIndex + 1 >= track.Measures.Count || IsSimileBar(track, measureIndex + 1) ||
                beat.StartSlots + beat.DurationSlots < measureSlots - PositionEpsilon) continue;
            var next = NextNoteInMeasure(beats, beat, note.Source, track);
            if (next is not null) continue; // Same-measure destination draws the full tie above.
            var nextBar = FindAdjacentBarTieDestination(track, measureIndex + 1, note.Source, track, voiceIndex);
            if (nextBar is { } destination && destination.StartSlots < PositionEpsilon &&
                (destination.Cell.IsTied || destination.Note.Tied))
                ties.Add(new StaffNotationTie(note.Source.Ghost ? note.X + HeadRadiusX + 3 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : note.X, note.Y,
                    note.Source.Ghost ? note.X + HeadRadiusX + 3 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : note.X, note.Y,
                    Above: !beat.StemUp, IsStub: true, TowardLeft: false));
        }
        return ties;
    }

    internal static (StaffNotationBeat Beat, StaffNotationNote Note)? PreviousNoteInMeasure(
        IReadOnlyList<StaffNotationBeat> beats, StaffNotationBeat current, TabNote target, TrackModel? track)
    {
        foreach (var beat in beats.Where(b => b.StartSlots < current.StartSlots - PositionEpsilon).OrderByDescending(b => b.StartSlots))
        {
            var note = beat.Notes.FirstOrDefault(n => SameTieIdentity(n.Source, target, track));
            if (note is not null)
                return Math.Abs(beat.StartSlots + beat.DurationSlots - current.StartSlots) < PositionEpsilon
                    ? (beat, note)
                    : null;
            if (UsesStringIdentity(track) && beat.Notes.Any(n => n.Source.StringIndex == target.StringIndex)) return null;
        }
        return null;
    }

    internal static TabNote? FindAdjacentBarNote(TrackModel? track, int measureIndex, TabNote target, TrackModel? identityTrack, int voiceIndex)
    {
        if (track is null || measureIndex < 0 || measureIndex >= track.Measures.Count) return null;
        var cells = track.Measures[measureIndex].CellsForVoice(voiceIndex);
        foreach (var pair in cells.Select((cell, index) => (Cell: cell, Index: index))
                     .Where(pair => pair.Cell.Notes.Count > 0)
                     .OrderByDescending(pair => pair.Cell.RhythmicPosition ?? pair.Index))
        {
            var note = pair.Cell.Notes.FirstOrDefault(n => SameTieIdentity(n, target, identityTrack));
            if (note is not null) return note;
            if (UsesStringIdentity(identityTrack) && pair.Cell.Notes.Any(n => n.StringIndex == target.StringIndex)) return null;
        }
        return null;
    }

    internal static (TabCell Cell, TabNote Note, double StartSlots)? FindAdjacentBarTieDestination(
        TrackModel track, int measureIndex, TabNote target, TrackModel? identityTrack, int voiceIndex)
    {
        if (measureIndex < 0 || measureIndex >= track.Measures.Count) return null;
        var measure = track.Measures[measureIndex];
        var cells = measure.CellsForVoice(voiceIndex);
        var events = cells.Select((cell, index) => (Cell: cell, Index: index))
            .Where(pair => pair.Cell.Notes.Count > 0 || pair.Cell.IsRest)
            .Select(pair => (pair.Cell, Start: pair.Cell.RhythmicPosition ?? pair.Index))
            .ToList();
        if (events.Count == 0) return null;
        var firstStart = events.Min(pair => pair.Start);
        foreach (var pair in events.Where(pair => Math.Abs(pair.Start - firstStart) < PositionEpsilon))
        {
            var note = pair.Cell.Notes.FirstOrDefault(n => SameTieIdentity(n, target, identityTrack));
            if (note is not null) return (pair.Cell, note, pair.Start);
        }
        return null;
    }

    internal static StaffNotationNote? NextNoteInMeasure(
        IReadOnlyList<StaffNotationBeat> beats, StaffNotationBeat current, TabNote target, TrackModel? track)
    {
        foreach (var beat in beats.Where(b => b.StartSlots > current.StartSlots + PositionEpsilon).OrderBy(b => b.StartSlots))
        {
            var note = beat.Notes.FirstOrDefault(n => SameTieIdentity(n.Source, target, track));
            if (note is not null) return note;
        }
        return null;
    }

    internal static bool UsesStringIdentity(TrackModel? track)
        => track?.Kind is TrackKind.Guitar or TrackKind.Bass;

    internal static bool SameTieIdentity(TabNote candidate, TabNote target, TrackModel? track)
        => candidate.MidiValue == target.MidiValue &&
           (!UsesStringIdentity(track) || candidate.StringIndex == target.StringIndex);

    internal static List<StaffNotationSlur> BuildHopoSlurs(IReadOnlyList<StaffNotationBeat> beats, double staffTop)
    {
        var result = new List<StaffNotationSlur>();
        foreach (var beat in beats)
        foreach (var note in beat.Notes.Where(n => n.Source.Techniques.Contains("HOPOOrigin")))
        {
            var previousOrigin = beats.Where(b => b.StartSlots < beat.StartSlots - PositionEpsilon)
                .SelectMany(b => b.Notes.Select(n => (Beat: b, Note: n)))
                .LastOrDefault(pair => pair.Note.Source.StringIndex == note.Source.StringIndex &&
                                       pair.Note.Source.Techniques.Contains("HOPOOrigin"));
            if (previousOrigin.Note is not null) continue;

            var destination = beats.Where(b => b.StartSlots > beat.StartSlots + PositionEpsilon)
                .SelectMany(b => b.Notes.Select(n => (Beat: b, Note: n)))
                .FirstOrDefault(pair => pair.Note.Source.StringIndex == note.Source.StringIndex &&
                                        pair.Note.Source.Techniques.Contains("HOPODestination"));
            if (destination.Note is null) continue;
            if (MakeHopoSlur(beat, note, destination.Beat, destination.Note, staffTop) is { } slur) result.Add(slur);
        }
        AddLegacyHopoSlurs(beats, staffTop, result);
        return result;
    }

    /// <summary>
    /// The editor's H toggle only sets a generic "HOPO" bit (no origin/destination pair). Same rule as the tab arc: the first such note
    /// on a string starts a phrase that runs over the following notes on that string that carry the bit; one slur from start to end.
    /// </summary>
    internal static void AddLegacyHopoSlurs(IReadOnlyList<StaffNotationBeat> beats, double staffTop, List<StaffNotationSlur> result)
    {
        static bool IsLegacy(TabNote n) => n.Techniques.Contains("HOPO") && !n.Techniques.Contains("HOPOOrigin") && !n.Techniques.Contains("HOPODestination");
        var ordered = beats.OrderBy(b => b.StartSlots).ToList();
        for (var i = 0; i < ordered.Count; i++)
        foreach (var note in ordered[i].Notes.Where(n => IsLegacy(n.Source)))
        {
            var stringIndex = note.Source.StringIndex;
            var startsPhrase = true;
            for (var j = i - 1; j >= 0 && startsPhrase; j--)
            {
                var earlier = ordered[j].Notes.FirstOrDefault(n => n.Source.StringIndex == stringIndex);
                if (earlier is null) continue;
                startsPhrase = !earlier.Source.Techniques.Contains("HOPO");
                break;
            }
            if (!startsPhrase) continue;
            (StaffNotationBeat Beat, StaffNotationNote Note)? last = null;
            for (var j = i + 1; j < ordered.Count; j++)
            {
                var next = ordered[j].Notes.FirstOrDefault(n => n.Source.StringIndex == stringIndex);
                if (next is null) continue;
                if (!next.Source.Techniques.Contains("HOPO")) break;
                last = (ordered[j], next);
            }
            if (last is { } end && MakeHopoSlur(ordered[i], note, end.Beat, end.Note, staffTop) is { } slur) result.Add(slur);
        }
    }

    internal static StaffNotationSlur? MakeHopoSlur(StaffNotationBeat beat, StaffNotationNote note,
        StaffNotationBeat destinationBeat, StaffNotationNote destinationNote, double staffTop)
    {
        var destination = (Beat: destinationBeat, Note: destinationNote);
        {
            if (destination.Beat.CenterX - beat.CenterX < 18) return null;
            // A destination with an accidental: the slur ends at the accidental's left edge instead of running through it.
            var endX = destination.Note.Accidental is not null ? destination.Beat.Notes.Min(n => n.X) - HeadRadiusX - 13 - GhostRoom(staffTop, destination.Beat) - destination.Note.AccidentalColumn * 10 : destination.Note.X;
            if (endX - note.X < 18) endX = destination.Note.X;   // too tight to stop short of the accidental (the arc would turn back on itself): keep the old head-to-head arc
            // Ghost brackets and a chord-mate's accidental at the slur's height: start / end clear of them (the stop-short case above already is).
            var (startInset, endInset) = ArcInsets(beat, note, destination.Beat, destination.Note, staffTop);
            var stoppedShort = endX != destination.Note.X;
            // ArcInsets measured its room against the destination head; a slur that already stops short has less, so a ghost start
            // inset could carry the start past the end. Keep the plain start then.
            if (stoppedShort && endX - 5 - (note.X + startInset) < 12) startInset = 5;
            return new StaffNotationSlur(note.X, note.Y, endX, destination.Note.Y, beat.StemUp, startInset, stoppedShort ? 5 : endInset);
        }
    }

    /// <summary>
    /// The reference's slide strokes in the staff: a short slanted line between the two heads of a shift / legato slide, a slash leading
    /// into a head (slide in from below / above) and a slash trailing from it (slide out up / down). Clear of the chord's accidentals and
    /// ghost bracket on the left, and of the ghost bracket and augmentation dots on the right.
    /// </summary>
    internal static List<StaffNotationSlideStroke> BuildSlideStrokes(TrackModel? track, int measureIndex, int voiceIndex,
        IReadOnlyList<StaffNotationBeat> beats, double staffTop)
    {
        var result = new List<StaffNotationSlideStroke>();
        if (track is null) return result;
        double Left(StaffNotationBeat beat) => beat.Notes.Min(n => n.X) - HeadRadiusX - 2 - GhostRoom(staffTop, beat)
            - (beat.Notes.Any(n => n.Accidental is not null) ? 11 + beat.Notes.Where(n => n.Accidental is not null).Max(n => n.AccidentalColumn) * 10 : 0);
        double Right(StaffNotationBeat beat, StaffNotationNote note) => note.X + HeadRadiusX + 2 + beat.Cell.Dots * 4 +
            (note.Source.Ghost ? 8 + (OnLedgerAt(staffTop, note) ? GhostLedgerPad : 0) : 0);
        foreach (var mark in TabSlideNotation.ForMeasure(track, measureIndex, voiceIndex))
        {
            var beat = beats.FirstOrDefault(b => b.CellIndex == mark.SourceCellIndex);
            var note = beat?.Notes.FirstOrDefault(n => ReferenceEquals(n.Source, mark.Source));
            if (beat is null || note is null) continue;
            switch (mark.Kind)
            {
                case TabSlideMarkKind.IncomingFromBelow:
                case TabSlideMarkKind.IncomingFromAbove:
                {
                    var below = mark.Kind == TabSlideMarkKind.IncomingFromBelow;
                    var x2 = Left(beat);
                    // Only as long as the room after the previous beat's ink (its head, ghost bracket, dots and stem) allows.
                    var previous = beats.Where(b => b.StartSlots < beat.StartSlots - PositionEpsilon && b.Notes.Count > 0).OrderByDescending(b => b.StartSlots).FirstOrDefault();
                    var length = Math.Min(7, x2 - (previous is null ? double.NegativeInfinity : previous.Notes.Max(n => Right(previous, n)) + 4));
                    if (length < 3.5) break;
                    result.Add(new StaffNotationSlideStroke(x2 - length, note.Y + (below ? 4.5 : -4.5) * length / 7, x2, note.Y + (below ? -0.5 : 0.5)));
                    break;
                }
                case TabSlideMarkKind.Connection when mark.TargetMeasureIndex == measureIndex &&
                    beats.FirstOrDefault(b => b.CellIndex == mark.TargetCellIndex) is { } destBeat &&
                    destBeat.Notes.FirstOrDefault(n => ReferenceEquals(n.Source, mark.Target)) is { } destNote &&
                    !beats.Any(b => b.Notes.Count > 0 && b.StartSlots > beat.StartSlots + PositionEpsilon && b.StartSlots < destBeat.StartSlots - PositionEpsilon) &&   // nothing between: a stroke never crosses another beat
                    Left(destBeat) - Right(beat, note) >= 8:
                    result.Add(new StaffNotationSlideStroke(Right(beat, note), note.Y, Left(destBeat), destNote.Y));
                    break;
                case TabSlideMarkKind.Connection:
                case TabSlideMarkKind.OutgoingUp:
                case TabSlideMarkKind.OutgoingDown:
                {
                    // No head to run to in this bar (or no room): a short trailing stroke in the slide's direction.
                    var up = mark.Kind == TabSlideMarkKind.OutgoingUp ||
                             mark.Kind == TabSlideMarkKind.Connection && (mark.Target is null || mark.Target.Fret >= mark.Source.Fret);   // same string: the fret gives the direction (as in the tab)
                    var x1 = Right(beat, note);
                    var next = beats.Where(b => b.StartSlots > beat.StartSlots + PositionEpsilon && b.Notes.Count > 0).OrderBy(b => b.StartSlots).FirstOrDefault();
                    var length = Math.Min(7, (next is null ? double.PositiveInfinity : Left(next) - 1.5 - x1));
                    if (length < 3.5) break;
                    result.Add(new StaffNotationSlideStroke(x1, note.Y + (up ? 0.5 : -0.5), x1 + length, note.Y + (up ? -4.5 : 4.5) * length / 7));
                    break;
                }
            }
        }
        return result;
    }

}
