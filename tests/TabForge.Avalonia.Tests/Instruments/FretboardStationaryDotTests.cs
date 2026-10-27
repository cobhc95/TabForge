using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the flicker guard for fast note changes: a dot that stays lit across rapid changes (every 30 ms for a second) is drawn at full
/// alpha on every frame.
/// Does not own: the lit-note selection (InstrumentLitNotes) or the dot drawing (InstrumentPanelView).
/// Tests: InstrumentPanelView lit dots across rapid changes.
/// </summary>
public class FretboardStationaryDotTests
{
    private const int StepMs = 30;
    private const int SpanMs = 1000;

    [AvaloniaFact]
    public void StationaryDotStaysAtFullAlphaAcrossRapidChanges()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var (first, second) = TwoLitSets(track);
        var both = first.Concat(second.Where(s => !first.Any(f => f.StringIndex == s.StringIndex && f.Fret == s.Fret))).ToArray();
        var panel = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard, Look = FretboardSettingsBinding.Look.From(new AppSettings()) };
        panel.Show(track, first);
        var layout = panel.FretboardLayout();
        var stationary = first.Select(n => FretboardGeometryRules.PositionOf(layout, n.StringIndex, n.Fret)).ToList();
        var nowColour = AvaloniaPalette.Get("InstrumentCurrent", false);
        for (var t = 0; t <= SpanMs; t += StepMs)
        {
            panel.Show(track, t / StepMs % 2 == 0 ? both : first);
            var canvas = new InstrumentFretNumberTests.RecordingDrawContext();
            panel.Draw(canvas, light: false);
            var alphas = canvas.Ellipses
                .Where(e => e.Fill is { } f && f.R == nowColour.R && f.G == nowColour.G && f.B == nowColour.B
                    && stationary.Any(c => Math.Abs(c.X - e.X) < 0.01 && Math.Abs(c.Y - e.Y) < 0.01))
                .Select(e => e.Fill!.Value.A)
                .ToList();
            Assert.Equal(first.Length, alphas.Count);
            Assert.All(alphas, a => Assert.Equal(nowColour.A, a));
        }
    }

    /// <summary>Two different non-empty lit sets from the demo song, the first one then the second.</summary>
    private static (InstrumentLitNotes.Lit[] First, InstrumentLitNotes.Lit[] Second) TwoLitSets(TrackModel track)
    {
        var sets = new List<InstrumentLitNotes.Lit[]>();
        for (var bar = 0; bar < track.Measures.Count; bar++)
            for (var cell = 0; cell < track.Measures[bar].Cells.Count; cell++)
            {
                var lit = InstrumentLitNotes.At(track, bar, cell).ToArray();
                if (lit.Length == 0 || (sets.Count > 0 && sets[^1].SequenceEqual(lit))) continue;
                sets.Add(lit);
                if (sets.Count == 2) return (sets[0], sets[1]);
            }
        throw new Xunit.Sdk.XunitException("the demo song needs two different chords");
    }
}
