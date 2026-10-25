using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Presets;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the table check that each Fretboard and Keyboard setting reaches the instrument panel: its Look always changes, and for the
/// rows that move geometry (frets, left-handed, string spacing, keyboard keys) the panel's drawn positions or hit tests change too.
/// Does not own: the dock side (ShowFretboard, FretboardAtBottom need a dock window) or the drawing of sizes and colours.
/// Tests: FretboardSettingsBinding, InstrumentPanelView.Look.
/// </summary>
public class FretboardSettingsBindingTests
{
    /// <summary>One settings row: the change, and the panel kind whose geometry it moves (null when only the look changes).</summary>
    private sealed record Row(string Name, Action<AppSettings> Change, InstrumentPanelKind? GeometryKind);

    private static readonly Row[] Rows =
    {
        new("FretboardFrets", s => s.Editing.FretboardFrets = 12, InstrumentPanelKind.Fretboard),
        new("LeftHanded", s => s.Editing.LeftHanded = true, InstrumentPanelKind.Fretboard),
        new("FretStringSpacing", s => s.Editing.FretStringSpacing = FretStringSpacings.Wide, InstrumentPanelKind.Fretboard),
        new("KeyboardKeys", s => s.Editing.KeyboardKeys = 49, InstrumentPanelKind.Keyboard),
        new("PreviewHorizon", s => s.Editing.PreviewHorizon = 2, null),
        new("PreviewNotesEnabled", s => s.Editing.PreviewNotesEnabled = false, null),
        new("FretNumberSize", s => s.Editing.FretNumberSize = FretNumberSizes.Small, null),
        new("FretMarkerSizePercent", s => s.Editing.FretMarkerSizePercent = 60, null),
        new("InstrumentView", s => s.Editing.InstrumentView = InstrumentViews.Keyboard, null),
        // The board fills an 800 DIP pane, so Right moves nothing here; the width test checks the position at wider panes.
        new("FretboardPosition", s => s.Appearance.FretboardPosition = "Right", null),
        new("LockInstrumentSize", s => s.Appearance.LockInstrumentSize = true, InstrumentPanelKind.Fretboard),
        new("ShowNoteNames", s => s.Editing.ShowNoteNames = true, null),
        new("FollowFretboard", s => s.Follow.FollowFretboard = false, null),
        new("ScaleHighlight", s => s.Editing.ScaleHighlight = "C Major", null),
        new("ScaleHighlightStyle", s => s.Editing.ScaleHighlightStyle = ScaleHighlightStyles.Rings, null),
        new("ScaleHighlightColour", s => s.Editing.ScaleHighlightColour = "Red", null),
        new("ScaleHighlightStrength", s => s.Editing.ScaleHighlightStrength = 50, null),
        new("KeyboardKeyColours", s => s.Editing.KeyboardKeyColours = KeyboardKeyStyles.Grey, null),
        new("FretMarkerColour", s => s.Editing.FretMarkerColour = "Amber", null),
        new("FretMarkerBrightness", s => s.Editing.FretMarkerBrightness = "Brightest", null),
    };

    [AvaloniaFact]
    public void EachFretboardSettingReachesThePanel()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        foreach (var row in Rows)
        {
            var baseline = new AppSettings();
            var changed = new AppSettings();
            row.Change(changed);
            var baselineLook = FretboardSettingsBinding.Look.From(baseline);
            var changedLook = FretboardSettingsBinding.Look.From(changed);
            Assert.True(baselineLook != changedLook, $"{row.Name} does not change the panel look");
            if (row.GeometryKind is not { } kind) continue;
            Assert.True(Draw(track, kind, baselineLook) != Draw(track, kind, changedLook), $"{row.Name} does not change the panel geometry");
        }
    }

    [AvaloniaTheory]
    [InlineData(900.0)]
    [InlineData(1300.0)]
    [InlineData(1900.0)]
    public void BoardStaysBesideTheLegendAndCentredByDefault(double width)
    {
        var panel = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard, Look = FretboardSettingsBinding.Look.From(new AppSettings()) };
        panel.Show(DemoSongFactory.Create().Tracks[0], Array.Empty<InstrumentLitNotes.Lit>());
        panel.Measure(new Size(width, 150));
        panel.Arrange(new Rect(0, 0, width, 150));
        var scale = panel.FretboardScale();
        var paneWidth = (width - InstrumentPanelView.CloseButtonReserve) / scale;
        var board = panel.FretboardLayout().Board;
        var leftMargin = board.X;
        var rightMargin = paneWidth - (board.Right + FretboardGeometryRules.LegendWidth);
        Assert.True(leftMargin >= 0, $"board starts outside the pane at {width}");
        Assert.True(rightMargin >= 0, $"board and legend run under the close X at {width}");
        Assert.True((board.Right + FretboardGeometryRules.LegendWidth) * scale <= width - InstrumentPanelView.CloseButtonReserve + 0.5);
        Assert.True(Math.Abs(leftMargin - rightMargin) <= FretboardGeometryRules.LeftGutter + FretboardGeometryRules.LegendWidth,
            $"board is not centred at {width}: left {leftMargin:F1}, right {rightMargin:F1}");
    }

    [AvaloniaFact]
    public void LitNowDotStaysPlayGreenWhileInlaysFollowTheMarkerColour()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var settings = new AppSettings();
        settings.Editing.FretMarkerColour = "Amber";
        var panel = new InstrumentPanelView { Kind = InstrumentPanelKind.Fretboard, Look = FretboardSettingsBinding.Look.From(settings) };
        panel.Show(track, FirstLitNotes(track));
        panel.Measure(new Size(800, 150));
        panel.Arrange(new Rect(0, 0, 800, 150));
        var canvas = new InstrumentFretNumberTests.RecordingDrawContext();
        panel.Draw(canvas, light: false);
        Assert.Contains(canvas.Ellipses, e => e.Fill == AvaloniaPalette.Get("InstrumentCurrent", false));
        Assert.Contains(canvas.Ellipses, e => e.Fill == AvaloniaPalette.Get("InstrumentNext", false));
    }

    private static InstrumentLitNotes.Lit[] FirstLitNotes(TrackModel track)
    {
        for (var bar = 0; bar < track.Measures.Count; bar++)
            for (var cell = 0; cell < track.Measures[bar].Cells.Count; cell++)
            {
                var lit = InstrumentLitNotes.At(track, bar, cell).ToArray();
                if (lit.Length > 0) return lit;
            }
        throw new Xunit.Sdk.XunitException("the demo song's first track has no notes");
    }

    /// <summary>The panel's drawn positions (fretboard) or key hit tests (keyboard) for a look, after a layout at a fixed size.</summary>
    private static string Draw(TrackModel track, InstrumentPanelKind kind, FretboardSettingsBinding.Look look)
    {
        var panel = new InstrumentPanelView { Kind = kind, Look = look };
        panel.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        panel.Measure(new Size(800, 150));
        panel.Arrange(new Rect(0, 0, 800, 150));
        var parts = new List<string>();
        if (kind == InstrumentPanelKind.Keyboard)
        {
            for (var x = 0.0; x < 800; x += 10) parts.Add(panel.Pick(new UiPoint(x, 100))?.Midi.ToString() ?? "-");
        }
        else
        {
            for (var s = 0; s < 6; s++)
                foreach (var fret in new[] { 1, 7, 12 })
                {
                    var at = panel.FretCentre(s, fret);
                    parts.Add($"{at.X:F2}/{at.Y:F2}");
                }
        }
        return string.Join(",", parts);
    }
}
