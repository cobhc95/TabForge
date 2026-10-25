using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Presets;

namespace TabForge;

// Owns: the one-bar-per-technique test song the Guitar Pro fixture checks build on.
// Does not own: the fixture itself (SelfTestGpFixture.cs).
// Tests: TestSyntheticGuitarProFixture.
public static partial class SelfTest
{
    /// <summary>
    /// A test song with one bar per technique (and several bend shapes): two clear quarter notes each, so the pitch of every technique can be
    /// measured through the engine. <paramref name="barNames"/> names each bar.
    /// </summary>
    private static SongProject TechniqueSong(out List<string> barNames)
    {
        var song = TemplateFactory.Blank();
        var track = song.Tracks[0];
        var bars = new List<(string Name, Action<TabNote> Apply)>();
        foreach (var name in GpEffects.All) bars.Add((name, n => n.Techniques.Add(name)));
        void Bend(string label, params (double Offset, double Value)[] points) =>
            bars.Add((label, n => { n.Techniques.Add("Bend"); n.BendPoints = points.Select(p => new BendPointModel { Offset = p.Offset, Value = p.Value }).ToList(); }));
        Bend("bend up 1 st", (0, 0), (15, 4), (60, 4));
        Bend("bend up 2 st", (0, 0), (15, 8), (60, 8));
        Bend("pre-bend 1 st", (0, 4), (60, 4));
        Bend("pre-bend and release", (0, 4), (30, 4), (45, 0), (60, 0));
        Bend("bend and release", (0, 0), (15, 4), (35, 4), (50, 0), (60, 0));
        Bend("bend up 3 st (wide)", (0, 0), (20, 12), (60, 12));
        track.Measures = TemplateFactory.Measures(bars.Count);
        for (var i = 0; i < bars.Count; i++)
            foreach (var cell in new[] { 0, 4 })
            {
                var c = track.Measures[i].Cells[cell];
                c.DurationDenominator = 4;
                var note = new TabNote { StringIndex = 2, Fret = bars[i].Name.Contains("Harmonic") ? 12 : 5, MidiValue = track.StringTunings[2] + (bars[i].Name.Contains("Harmonic") ? 12 : 5) };
                bars[i].Apply(note);
                c.Notes.Add(note);
            }
        song.Title = "technique test: " + string.Join(", ", bars.Select(b => b.Name).Take(3)) + "…";
        barNames = bars.Select(b => b.Name).ToList();
        return song;
    }
}
