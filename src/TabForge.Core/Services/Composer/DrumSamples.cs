using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Composer drum-sample catalogue: six starter grooves in four genres as one-bar 16-step patterns (kick, snare and hi-hat) with a suggested tempo.
// Does not own: auditioning them (the window plays them at the song's tempo), placing them on a track (DrumSampleDrop), or the panel that lists them.
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests.
/// <summary>The genre a Composer drum groove is filed under in the panel.</summary>
public enum DrumGenre
{
    RockPop,
    Metal,
    PunkHardcore,
    FunkBreakbeat,
}

/// <summary>The display names and panel order of the Composer drum genres.</summary>
public static class DrumGenreNames
{
    /// <summary>The genres in panel order.</summary>
    public static IReadOnlyList<DrumGenre> Order { get; } =
        [DrumGenre.RockPop, DrumGenre.Metal, DrumGenre.PunkHardcore, DrumGenre.FunkBreakbeat];

    /// <summary>The display name of <paramref name="genre"/>.</summary>
    public static string Of(DrumGenre genre) => genre switch
    {
        DrumGenre.RockPop => "Rock/Pop",
        DrumGenre.Metal => "Metal",
        DrumGenre.PunkHardcore => "Punk/Hardcore",
        DrumGenre.FunkBreakbeat => "Funk/Breakbeat",
        _ => genre.ToString(),
    };
}

/// <param name="Genre">The genre the groove is filed under in the panel.</param>
public sealed record DrumSample(string Id, string Name, int SuggestedBpm, int Bars,
    IReadOnlyList<int> KickSteps, IReadOnlyList<int> SnareSteps, IReadOnlyList<int> HatSteps, DrumGenre Genre)
{
    /// <summary>Steps per bar: the 16th-note grid the patterns are written on.</summary>
    public const int StepsPerBar = 16;

    /// <summary>General MIDI drum notes for the three kit pieces.</summary>
    public const int KickMidi = 36, SnareMidi = 38, HatMidi = 42;

    /// <summary>Every hit of the sample in order: bar, step, GM drum note.</summary>
    public IEnumerable<(int Bar, int Step, int Midi, int Velocity)> Hits()
    {
        for (var bar = 0; bar < Bars; bar++)
        {
            foreach (var step in KickSteps) yield return (bar, step, KickMidi, VelocityAt(KickMidi, step, bar));
            foreach (var step in SnareSteps) yield return (bar, step, SnareMidi, VelocityAt(SnareMidi, step, bar));
            foreach (var step in HatSteps) yield return (bar, step, HatMidi, VelocityAt(HatMidi, step, bar));
        }
    }

    /// <summary>The velocity of one hit, from a fixed accent pattern (never random, and timing never moves): the metric position sets the
    /// dynamic — quarter-note positions strongest, 8th-note positions a step below, 16th off-beats a step below that — the kit piece sets
    /// its weight (the snare's backbeats on steps 4 and 12 and the kick's bar-opening hit sit a step above), and in a multi-bar sample the
    /// last bar lifts its quarter-note hits a step, pushing into the repeat. Levels are the shared dynamics table's velocities
    /// (mp 64, mf 80, f 95, ff 112), the values staff dynamics and Guitar Pro export already speak, so a dropped tab keeps the shape.</summary>
    public int VelocityAt(int midi, int step, int bar)
    {
        var level = midi switch
        {
            KickMidi => step == 0 ? 6 : step % 4 == 0 ? 5 : step % 2 == 0 ? 4 : 3,
            SnareMidi => step is 4 or 12 ? 6 : step % 4 == 0 ? 5 : step % 2 == 0 ? 4 : 3,
            _ => step % 4 == 0 ? 5 : step % 2 == 0 ? 4 : 3,
        };
        if (Bars > 1 && bar == Bars - 1 && step % 4 == 0) level = Math.Min(6, level + 1);
        return Dynamics.Velocities[level];
    }

    /// <summary>The GM drum notes struck on <paramref name="step"/> of a bar (every bar repeats the pattern).</summary>
    public IEnumerable<int> NotesAt(int step)
    {
        if (KickSteps.Contains(step)) yield return KickMidi;
        if (SnareSteps.Contains(step)) yield return SnareMidi;
        if (HatSteps.Contains(step)) yield return HatMidi;
    }

    /// <summary>The sample as drum-source tab cells (one cell per 16th slot, all bars): the input the paste mapper turns into the target track's notes.
    /// A hit cell lasts until the next hit of its bar (or the bar line), so a sparse groove still fills its bars: a lone 16th in an
    /// otherwise empty stretch would leave the bar short, and the score marks short bars red.</summary>
    public List<TabCell> Cells()
    {
        var hitSteps = KickSteps.Concat(SnareSteps).Concat(HatSteps).Distinct().OrderBy(s => s).ToList();
        var cells = new List<TabCell>(Bars * StepsPerBar);
        for (var bar = 0; bar < Bars; bar++)
            for (var step = 0; step < StepsPerBar; step++)
            {
                var cell = new TabCell();
                if (KickSteps.Contains(step)) cell.Notes.Add(new TabNote { StringIndex = 0, Fret = KickMidi, MidiValue = KickMidi, Velocity = VelocityAt(KickMidi, step, bar) });
                if (SnareSteps.Contains(step)) cell.Notes.Add(new TabNote { StringIndex = 0, Fret = SnareMidi, MidiValue = SnareMidi, Velocity = VelocityAt(SnareMidi, step, bar) });
                if (HatSteps.Contains(step)) cell.Notes.Add(new TabNote { StringIndex = 0, Fret = HatMidi, MidiValue = HatMidi, Velocity = VelocityAt(HatMidi, step, bar) });
                if (cell.Notes.Count > 0)
                {
                    var next = hitSteps.FirstOrDefault(s => s > step, StepsPerBar);
                    (cell.DurationDenominator, cell.Dots) = DurationFor(next - step);
                }
                cells.Add(cell);
            }
        return cells;
    }

    /// <summary>The written value spanning <paramref name="slots"/> 16th slots: the exact dotted value where one exists
    /// (every starter groove's gaps have one), else the longest plain value that fits.</summary>
    private static (int Denominator, int Dots) DurationFor(int slots) => slots switch
    {
        1 => (16, 0),
        2 => (8, 0),
        3 => (8, 1),
        4 => (4, 0),
        6 => (4, 1),
        8 => (2, 0),
        12 => (2, 1),
        >= 16 => (1, 0),
        >= 8 => (2, 0),
        >= 4 => (4, 0),
        _ => (16, 0),
    };
}

/// <summary>The starter drum grooves the Composer panel offers.</summary>
public static class DrumSamples
{
    private static readonly DrumSample[] StarterSamples =
    {
        new("straight-8ths", "Straight 8ths groove", 120, 4,
            new[] { 0, 8 }, new[] { 4, 12 }, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, DrumGenre.RockPop),
        new("half-time-chug", "Half-time chug", 90, 4,
            new[] { 0, 10 }, new[] { 8 }, new[] { 0, 8 }, DrumGenre.Metal),
        new("blast-16ths", "Blast 16ths", 200, 2,
            new[] { 0, 6, 8, 14 }, new[] { 4, 12 }, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }, DrumGenre.Metal),
        new("gallop-fill", "Gallop fill", 160, 1,
            new[] { 0, 6, 8, 14 }, new[] { 3, 11 }, new[] { 0, 3, 6, 8, 11, 14 }, DrumGenre.Metal),
        new("d-beat", "D-beat", 180, 4,
            new[] { 0, 3, 8, 11 }, new[] { 4, 12, 14 }, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }, DrumGenre.PunkHardcore),
        new("breakbeat", "Breakbeat", 110, 2,
            new[] { 0, 7, 10 }, new[] { 4, 12, 15 }, new[] { 0, 4, 6, 10, 14 }, DrumGenre.FunkBreakbeat),
    };

    /// <summary>The samples in catalogue order.</summary>
    public static IReadOnlyList<DrumSample> All => StarterSamples;

    /// <summary>The genres that have at least one sample, in <see cref="DrumGenreNames.Order"/> order.</summary>
    public static IReadOnlyList<DrumGenre> GenresPresent =>
        DrumGenreNames.Order.Where(genre => StarterSamples.Any(sample => sample.Genre == genre)).ToList();

    /// <summary>The samples of <paramref name="genre"/>, in catalogue order.</summary>
    public static IReadOnlyList<DrumSample> InGenre(DrumGenre genre) =>
        StarterSamples.Where(sample => sample.Genre == genre).ToList();

    /// <summary>The sample with <paramref name="id"/>, or null.</summary>
    public static DrumSample? ById(string id) => StarterSamples.FirstOrDefault(s => s.Id == id);
}
