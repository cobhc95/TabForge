using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Composer drum-sample catalogue: starter grooves in four genres as 16-step patterns over any
//   General MIDI drum piece (kick, snare, hi-hat, ride, crash, toms…) with a suggested tempo.
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
    IReadOnlyList<(int Midi, int[] Steps)> Parts, DrumGenre Genre)
{
    /// <summary>Steps per bar: the 16th-note grid the patterns are written on.</summary>
    public const int StepsPerBar = 16;

    /// <summary>General MIDI drum notes for the kit pieces a starter groove can use (GM standard channel-10 notes).
    /// Grooves are not limited to these: any GM note 35–81 from <see cref="DrumMaps"/> can appear in Parts.</summary>
    public const int KickMidi = 36, SnareMidi = 38, HatMidi = 42, RideMidi = 51, CrashMidi = 49, OpenHatMidi = 46;

    /// <summary>The two back-compat accessors.</summary>
    public IReadOnlyList<int> KickSteps => Parts.FirstOrDefault(p => p.Midi == KickMidi).Steps ?? Array.Empty<int>();
    public IReadOnlyList<int> SnareSteps => Parts.FirstOrDefault(p => p.Midi == SnareMidi).Steps ?? Array.Empty<int>();
    public IReadOnlyList<int> HatSteps => Parts.FirstOrDefault(p => p.Midi == HatMidi).Steps ?? Array.Empty<int>();

    /// <summary>One kit piece's display label for the panel's pattern grid.</summary>
    public static string PartLabel(int midi) => midi switch
    {
        KickMidi => "BD", SnareMidi => "SD", HatMidi => "HH", RideMidi => "RC", CrashMidi => "CC", OpenHatMidi => "OH",
        _ => midi.ToString(),
    };

    /// <summary>Every hit of the sample in order: bar, step, GM drum note.</summary>
    public IEnumerable<(int Bar, int Step, int Midi, int Velocity)> Hits()
    {
        for (var bar = 0; bar < Bars; bar++)
            foreach (var (midi, steps) in Parts)
                foreach (var step in steps)
                    yield return (bar, step, midi, VelocityAt(midi, step, bar));
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
        foreach (var (midi, steps) in Parts)
            if (steps.Contains(step)) yield return midi;
    }

    /// <summary>The sample as drum-source tab cells (one cell per 16th slot, all bars): the input the paste mapper turns into the target track's notes.
    /// A hit cell lasts until the next hit of its bar (or the bar line), so a sparse groove still fills its bars: a lone 16th in an
    /// otherwise empty stretch would leave the bar short, and the score marks short bars red.</summary>
    public List<TabCell> Cells()
    {
        var hitSteps = Parts.SelectMany(p => p.Steps).Distinct().OrderBy(s => s).ToList();
        var cells = new List<TabCell>(Bars * StepsPerBar);
        for (var bar = 0; bar < Bars; bar++)
            for (var step = 0; step < StepsPerBar; step++)
            {
                var cell = new TabCell();
                foreach (var (midi, steps) in Parts)
                    if (steps.Contains(step))
                        cell.Notes.Add(new TabNote { StringIndex = 0, Fret = midi, MidiValue = midi, Velocity = VelocityAt(midi, step, bar) });
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
    private static readonly IReadOnlyList<DrumSample> StarterSamples = new DrumSample[]
    {
        new("straight-8ths", "Straight 8ths groove", 120, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.RockPop),
        new("half-time-chug", "Half-time chug", 90, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("blast-16ths", "Blast 16ths", 200, 2,
            new[] { (36, new[] { 0, 6, 8, 14 }), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("gallop-fill", "Gallop fill", 160, 1,
            new[] { (36, new[] { 0, 6, 8, 14 }), (38, new[] { 3, 11 }), (42, new[] { 0, 3, 6, 8, 11, 14 }) }, DrumGenre.Metal),
        new("d-beat", "D-beat", 180, 4,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12, 14 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("breakbeat", "Breakbeat", 110, 2,
            new[] { (36, new[] { 0, 7, 10 }), (38, new[] { 4, 12, 15 }), (42, new[] { 0, 4, 6, 10, 14 }) }, DrumGenre.FunkBreakbeat),
        new("ride-rock", "Ride rock", 132, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.RockPop),
        new("crash-funk", "Crash funk", 112, 2,
            new[] { (36, new[] { 0, 7, 10 }), (38, new[] { 4, 12, 15 }), (42, new[] { 0, 2, 4, 6, 10, 12, 14 }), (49, new[] { 0 }) }, DrumGenre.FunkBreakbeat),
        new("tom-break", "Tom break", 126, 1,
            new[] { (36, new[] { 0 }), (38, new[] { 12 }), (42, new[] { 0, 4, 8 }), (48, new[] { 8, 10 }), (47, new[] { 12, 14 }) }, DrumGenre.RockPop),
        // --- Rock/Pop additions: ballads, intros, mid-tempo and rock-driven ---
        new("ballad-soft", "Ballad soft", 72, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.RockPop),
        new("ballad-intro", "Ballad intro", 64, 4,
            new[] { (36, new[] { 0 }), (38, new[] { 8 }), (42, new[] { 0, 8 }) }, DrumGenre.RockPop),
        new("pop-16ths", "Pop 16ths", 128, 2,
            new[] { (36, new[] { 0, 6, 8, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }) }, DrumGenre.RockPop),
        new("rock-driving", "Rock driving", 140, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.RockPop),
        new("power-ballad", "Power ballad", 88, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 4, 8, 12 }), (49, new[] { 0 }) }, DrumGenre.RockPop),
        new("acoustic-light", "Acoustic light", 96, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (51, new[] { 0, 4, 8, 12 }) }, DrumGenre.RockPop),
        new("stadium-stomp", "Stadium stomp", 118, 4,
            new[] { (36, new[] { 0, 2, 8, 10 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.RockPop),
        new("slow-rock", "Slow rock", 82, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (51, new[] { 0, 8 }), (49, new[] { 0 }) }, DrumGenre.RockPop),
        new("introspective", "Introspective", 76, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.RockPop),
        new("arena-rock", "Arena rock", 150, 2,
            new[] { (36, new[] { 0, 4, 8, 12 }), (38, new[] { 4, 12 }), (49, new[] { 0 }) }, DrumGenre.RockPop),
        new("punchy-pop", "Punchy pop", 124, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (49, new[] { 0 }) }, DrumGenre.RockPop),
        new("mellow-intro", "Mellow intro", 60, 4,
            new[] { (36, new[] { 0 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.RockPop),
        new("classic-rock", "Classic rock", 136, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.RockPop),
        // --- Metal additions: double bass, half-time, blast variants, ballads/intros ---
        new("double-bass", "Double bass", 180, 2,
            new[] { (36, Enumerable.Range(0, 8).Select(i => i * 2).ToArray()), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 8).Select(i => i * 2).ToArray()) }, DrumGenre.Metal),
        new("double-bass-16ths", "Double bass 16ths", 200, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 8).Select(i => i * 2).ToArray()) }, DrumGenre.Metal),
        new("gallop-metal", "Gallop metal", 172, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 3, 6, 8, 11, 14 }) }, DrumGenre.Metal),
        new("blast-beat", "Blast beat", 220, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("half-time-metal", "Half-time metal", 84, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 8 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("doom-slow", "Doom slow", 60, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (51, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("thrash-8ths", "Thrash 8ths", 186, 2,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("crash-blast", "Crash blast", 210, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (49, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("metal-ballad", "Metal ballad", 78, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (42, new[] { 0, 8 }), (51, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("intro-toms", "Intro toms", 108, 4,
            new[] { (36, new[] { 0 }), (42, new[] { 0, 4 }), (48, new[] { 2, 6 }), (47, new[] { 10, 14 }) }, DrumGenre.Metal),
        new("power-metal", "Power metal", 165, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("single-bass-16ths", "Single bass 16ths", 170, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("slow-heavy", "Slow heavy", 70, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("epic-rock", "Epic rock", 145, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        // --- Metal extras round 2: double-bass, blast and thrash variants, ballads/intros ---
        new("double-bass-triplets", "Double bass triplets", 192, 2,
            new[] { (36, new[] { 0, 2, 3, 4, 6, 7, 8, 10, 11, 12, 14, 15 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("thrash-blast", "Thrash blast", 205, 2,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("gravity-blast", "Gravity blast", 230, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("death-blast-hh", "Death blast HH", 215, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("half-time-blast", "Half-time blast", 150, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 8 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("slow-double-bass", "Slow double bass", 100, 4,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, new[] { 8 }), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("crash-thrash", "Crash thrash", 195, 2,
            new[] { (36, new[] { 0, 4, 8, 12 }), (38, new[] { 4, 12 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("ride-blast", "Ride blast", 208, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (51, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("gallop-heavy", "Gallop heavy", 158, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("metal-intro-clean", "Metal intro clean", 88, 4,
            new[] { (36, new[] { 0, 8 }), (42, new[] { 0, 4, 8, 12 }), (51, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("metal-ballad-2", "Metal ballad 2", 74, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("tom-blast", "Tom blast", 190, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 8 }), (48, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("floor-tom-thrash", "Floor tom thrash", 168, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (41, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("doom-crash", "Doom crash", 56, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("epic-gallop", "Epic gallop", 142, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (51, new[] { 0, 3, 6, 8, 11, 14 }) }, DrumGenre.Metal),
        new("power-gallop", "Power gallop", 176, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 3, 6, 8, 11, 14 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("chug-double", "Chug double", 132, 2,
            new[] { (36, new[] { 0, 2, 6, 8, 10, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("triple-kick", "Triple kick", 185, 2,
            new[] { (36, new[] { 0, 1, 2, 6, 8, 9, 10, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("blast-snare", "Blast snare", 200, 2,
            new[] { (36, Enumerable.Range(0, 8).Select(i => i * 2).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("marching-blast", "Marching blast", 140, 4,
            new[] { (36, new[] { 0, 4, 8, 12 }), (38, new[] { 0, 2, 4, 6, 10, 12, 14 }), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("industrial-chug", "Industrial chug", 122, 4,
            new[] { (36, new[] { 0, 3, 8, 12 }), (38, new[] { 8 }), (42, new[] { 0, 2, 4, 8 }) }, DrumGenre.Metal),
        new("black-blast", "Black blast", 240, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("melodic-metal", "Melodic metal", 148, 2,
            new[] { (36, new[] { 0, 6, 8, 14 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("symphonic-metal", "Symphonic metal", 136, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (49, new[] { 0 }), (51, new[] { 2, 6, 10, 14 }) }, DrumGenre.Metal),
        new("metal-acoustic", "Metal acoustic", 96, 4,
            new[] { (36, new[] { 0, 8 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("crash-finale", "Crash finale", 160, 2,
            new[] { (36, new[] { 0, 4, 8, 12 }), (38, new[] { 4, 12 }), (49, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("snake-blast", "Snake blast", 198, 2,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, Enumerable.Range(0, 16).ToArray()), (42, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("hammered-chug", "Hammered chug", 92, 4,
            new[] { (36, new[] { 0, 2, 4, 6 }), (38, new[] { 8 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("frenzy-blast", "Frenzy blast", 225, 2,
            new[] { (36, new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("crush-groove", "Crush groove", 108, 4,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        new("emotional-metal", "Emotional metal", 82, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 8 }), (42, new[] { 0, 8 }), (51, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("heroic-thrash", "Heroic thrash", 154, 2,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12, 15 }), (42, new[] { 0, 3, 6, 8, 11, 14 }) }, DrumGenre.Metal),
        new("war-blast", "War blast", 262, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("veil-metal", "Veil metal", 102, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 8, 12 }) }, DrumGenre.Metal),
        new("labyrinth-blast", "Labyrinth blast", 188, 2,
            new[] { (36, new[] { 0, 1, 2, 4, 6, 8, 9, 10, 12, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.Metal),
        new("last-ride", "Last ride", 118, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.Metal),
        new("epic-outro", "Epic outro", 84, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 4, 8, 12 }), (49, new[] { 0, 8 }) }, DrumGenre.Metal),
        new("frozen-blast", "Frozen blast", 246, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.Metal),
        new("titanic-gallop", "Titanic gallop", 168, 4,
            new[] { (36, new[] { 0, 3, 6, 8, 11, 14 }), (38, new[] { 4, 12 }), (49, new[] { 0 }) }, DrumGenre.Metal),
        // --- Punk/Hardcore additions ---
        new("punk-8ths", "Punk 8ths", 188, 2,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("hardcore-blast", "Hardcore blast", 224, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, Enumerable.Range(0, 16).ToArray()), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.PunkHardcore),
        new("ska-punk", "Ska punk", 172, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 2, 6, 10, 14 }) }, DrumGenre.PunkHardcore),
        new("melodic-punk", "Melodic punk", 165, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("anarchy-drive", "Anarchy drive", 198, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12, 14 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("mosh-call", "Mosh call", 92, 4,
            new[] { (36, new[] { 0, 2, 8, 10 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.PunkHardcore),
        new("punk-intro", "Punk intro", 140, 4,
            new[] { (36, new[] { 0, 8 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("breakdown", "Breakdown", 88, 4,
            new[] { (36, new[] { 0, 3, 4, 8, 11, 12 }), (38, new[] { 8 }), (42, new[] { 0, 8 }) }, DrumGenre.PunkHardcore),
        new("crash-punk", "Crash punk", 176, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (49, new[] { 0 }), (42, new[] { 0, 2, 4, 6, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        new("fast-punk", "Fast punk", 210, 2,
            new[] { (36, Enumerable.Range(0, 16).ToArray()), (38, new[] { 4, 12 }), (42, Enumerable.Range(0, 16).ToArray()) }, DrumGenre.PunkHardcore),
        new("hardcore-groove", "Hardcore groove", 130, 4,
            new[] { (36, new[] { 0, 7, 8, 14 }), (38, new[] { 4, 12 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.PunkHardcore),
        new("pogo-beat", "Pogo beat", 156, 2,
            new[] { (36, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }), (38, new[] { 2, 6, 10, 14 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.PunkHardcore),
        new("street-punk", "Street punk", 142, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.PunkHardcore),
        // --- Funk/Breakbeat additions ---
        new("funk-8ths", "Funk 8ths", 104, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.FunkBreakbeat),
        new("boogie", "Boogie", 116, 2,
            new[] { (36, new[] { 0, 3, 8, 11 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.FunkBreakbeat),
        new("neo-soul", "Neo soul", 84, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 4, 8, 12 }), (51, new[] { 0 }) }, DrumGenre.FunkBreakbeat),
        new("jazz-ride", "Jazz ride", 148, 2,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (51, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.FunkBreakbeat),
        new("swing", "Swing", 132, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.FunkBreakbeat),
        new("gospel-chops", "Gospel chops", 96, 2,
            new[] { (36, new[] { 0, 7, 10 }), (38, new[] { 4, 12, 14 }), (42, new[] { 0, 4, 6, 10, 14 }), (49, new[] { 0 }) }, DrumGenre.FunkBreakbeat),
        new("hiphop-90s", "Hip hop 90s", 92, 4,
            new[] { (36, new[] { 0, 10 }), (38, new[] { 8 }), (42, new[] { 0, 8 }) }, DrumGenre.FunkBreakbeat),
        new("soul-70s", "Soul 70s", 100, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 2, 4, 6, 8, 10, 12, 14 }) }, DrumGenre.FunkBreakbeat),
        new("breakbeat-2", "Breakbeat 2", 118, 2,
            new[] { (36, new[] { 0, 3, 10 }), (38, new[] { 4, 12, 15 }), (42, new[] { 0, 6, 10, 14 }) }, DrumGenre.FunkBreakbeat),
        new("funk-intro", "Funk intro", 102, 4,
            new[] { (36, new[] { 0, 8 }), (42, new[] { 0, 4, 8, 12 }) }, DrumGenre.FunkBreakbeat),
        new("clinic-funk", "Clinic funk", 108, 2,
            new[] { (36, new[] { 0, 7, 10 }), (38, new[] { 4, 12, 15 }), (51, new[] { 0, 4, 8, 12 }) }, DrumGenre.FunkBreakbeat),
        new("disco-pulse", "Disco pulse", 126, 2,
            new[] { (36, new[] { 0, 4, 8, 12 }), (38, new[] { 4, 12 }), (46, new[] { 2, 6, 10, 14 }) }, DrumGenre.FunkBreakbeat),
        new("lazy-groove", "Lazy groove", 80, 4,
            new[] { (36, new[] { 0, 8 }), (38, new[] { 4, 12 }), (42, new[] { 0, 8 }) }, DrumGenre.FunkBreakbeat),
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
