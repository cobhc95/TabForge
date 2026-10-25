using System;
using System.Collections.Generic;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the test that each Score page row, changed in the settings, changes the score view's value after the binding applies it.
/// Does not own: the binding (ScoreSettingsBinding), the score drawing (ScoreRenderer), the settings file (AppSettingsStore).
/// Tests: ScoreSettingsBinding, ScoreText, ScoreAppearance.
/// </summary>
public class ScoreSettingsBindingTests
{
    // One row per Score page setting: the change to make and the score value it must move.
    private static readonly (string Row, Action<AppSettings> Change, Func<ScorePreviewView, object> Read)[] Rows =
    [
        ("ScoreFontFamily", s => s.Appearance.ScoreFontFamily = "Cambria", _ => ScoreText.FontFamily),
        ("ScoreTextSize", s => s.Appearance.ScoreTextSize = 18, _ => ScoreText.TextSize),
        ("ScoreTextBold", s => s.Appearance.ScoreTextBold = true, _ => ScoreText.TextBold),
        ("ScoreTextItalic", s => s.Appearance.ScoreTextItalic = true, _ => ScoreText.TextItalic),
        ("ScoreSpacing", s => s.Appearance.ScoreSpacing = 1.3, v => v.Probe.Appearance.ScoreSpacing),
        ("SystemVerticalSpacing", s => s.Appearance.SystemVerticalSpacing = 1.4, v => v.Probe.Appearance.SystemVerticalSpacing),
        ("MeasureHorizontalSpacing", s => s.Appearance.MeasureHorizontalSpacing = 1.3, v => v.Probe.Appearance.MeasureHorizontalSpacing),
        ("LedgerLines", s => s.Appearance.LedgerLines = "Hidden", v => v.Probe.Appearance.LedgerLines),
        ("StaffLineOpacity", s => s.Appearance.StaffLineOpacity = 0.5, v => v.Probe.Appearance.StaffLineColor),
        ("ShowScoreBarNumbers", s => s.Appearance.ShowScoreBarNumbers = false, v => v.Probe.Appearance.ShowBarNumbers),
        ("ScoreBarNumberFrequency", s => s.Appearance.ScoreBarNumberFrequency = 4, v => v.Probe.Appearance.BarNumberFrequency),
        ("ShowSectionHeadings", s => s.Appearance.ShowSectionHeadings = false, v => v.Probe.Appearance.ShowSectionHeadings),
        ("ShowDynamics", s => s.Appearance.ShowDynamics = false, v => v.Probe.Appearance.ShowDynamics),
        ("HoverHighlightIntensity", s => s.Appearance.HoverHighlightIntensity = 0.6, v => v.Probe.Appearance.HoverHighlightIntensity),
        ("ShowNoteOutline", s => s.Appearance.ShowNoteOutline = false, v => v.Probe.Appearance.ShowNoteOutline),
        ("SelectionHighlightIntensity", s => s.Appearance.SelectionHighlightIntensity = 0.6, v => v.Probe.Appearance.SelectionHighlightIntensity),
        ("DarkScorePaperColour", s => s.Appearance.DarkScorePaperColour = "#202020", v => v.Probe.Appearance.DarkPaperColor),
        ("LightScorePaperColour", s => s.Appearance.LightScorePaperColour = "#F0F0F0", v => v.Probe.Appearance.LightPaperColor),
        ("DarkScoreInkColour", s => s.Appearance.DarkScoreInkColour = "#FF0000", v => v.Probe.Appearance.DarkInkColor),
        ("LightScoreInkColour", s => s.Appearance.LightScoreInkColour = "#0000FF", v => v.Probe.Appearance.LightInkColor),
        ("DarkScoreLinesColour", s => s.Appearance.DarkScoreLinesColour = "#00FF00", v => v.Probe.Appearance.DarkStaffLineColor),
        ("LightScoreLinesColour", s => s.Appearance.LightScoreLinesColour = "#00FF00", v => v.Probe.Appearance.LightStaffLineColor),
        ("CursorColour", s => s.Appearance.CursorColour = "#123456", v => v.Probe.Appearance.CursorColor),
        ("HoverColour", s => s.Appearance.HoverColour = "#FF0000", v => v.Probe.Appearance.HoverColor),
        ("PreferredContinuousScoreView", s => s.PreferredContinuousScoreView = false, v => v.Probe.Appearance.CenterSystems),
        ("PreferredHorizontalScoreView", s => s.PreferredHorizontalScoreView = true, v => v.Probe.HorizontalScroll),
    ];

    [AvaloniaFact]
    public void PointerOverABeatExposesAHoverCellAndLeavingClearsIt()
    {
        var score = new ScorePreviewView();
        var editor = score.Editor;
        var probe = score.Probe;
        var found = false;
        // Scan the demo score's grid for a point over a beat; the first one that hovers ends the search.
        for (var y = 40.0; y < 700 && !found; y += 8)
            for (var x = 120.0; x < 1200 && !found; x += 8)
                found = editor.HoverAt(x, y) && probe.HoverMeasure >= 0 && probe.HoverCell >= 0;
        Assert.True(found, "No beat under the pointer on the demo score.");
        Assert.True(editor.HoverEnd());
        Assert.Equal(-1, probe.HoverMeasure);
    }

    [AvaloniaFact]
    public void DefaultSettingsDrawNoSelectionOverTheScore()
    {
        var settings = new AppSettings();
        var score = new ScorePreviewView();
        _ = new ScoreSettingsBinding(score, settings);
        Assert.False(score.Probe.Selection.HasSelection, "A fresh score view with default settings has a selection range.");
    }

    [AvaloniaFact]
    public void EachScorePageRowChangesTheScoreView()
    {
        try
        {
            foreach (var (row, change, read) in Rows)
            {
                var settings = new AppSettings();
                var score = new ScorePreviewView();
                var binding = new ScoreSettingsBinding(score, settings);
                var before = read(score);
                change(settings);
                binding.Apply(settings);
                Assert.False(Equals(before, read(score)), $"Score row {row} did not change the score view.");
            }
        }
        finally
        {
            // ScoreText is process-wide; put the default style back for the other tests.
            new ScoreSettingsBinding(new ScorePreviewView(), new AppSettings());
        }
    }
}
