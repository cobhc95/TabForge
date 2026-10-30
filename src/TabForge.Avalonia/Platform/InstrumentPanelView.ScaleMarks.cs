using System;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: how the scale highlight is drawn on the fretboard and the keyboard: the five styles (dots, rings, cell tint, note names, degrees), the opacity,
//   the stronger root, the dimming of notes outside the scale and the "only on the frets in view" limit. The marks are drawn before the played notes, so
//   the now / next / upcoming / recent colours always sit on top of them.
// Does not own: the scale choice (EditingSettings.ScaleHighlight), the look values (FretboardSettingsBinding.Look), the played-note drawing.
// Tests: TabForge.Avalonia.Tests.ScaleHighlightDrawTests.
public sealed partial class InstrumentPanelView
{
    // Sizes in virtual DIPs / shares; the opacity setting is the alpha, these only shape the marks.
    private const double ScaleMarkMaxRadius = 11, ScaleRingWidth = 1.5, ScaleRootRingWidth = 2.4, ScaleDotShare = 0.7, ScaleRootDotShare = 0.85;
    private const double ScaleCellShare = 0.6, ScaleLabelShare = 1.0, ScaleDimAlpha = 0.45, ScaleLabelFontShare = 1.15;

    private static string DegreeLabel(int degree) => degree switch { 1 => "1", 2 => "2", 3 => "3", 4 => "4", 5 => "5", 6 => "6", 7 => "7", 8 => "8", 9 => "9", _ => "·" };

    private bool ScaleActive => _look.ScalePitches.Length == 12;
    private bool InScale(int pitchClass) => ScaleActive && _look.ScalePitches[pitchClass] == '1';

    /// <summary>The highlight colour: the custom colour when one is chosen, else the theme token.</summary>
    private UiColor ScaleInk(Func<string, UiColor> ink) => _look.ScaleCustom ?? ink(_look.ScaleColourKey);

    private double ScaleAlpha(int pitchClass, double share = 1) =>
        ScaleHighlightStyles.MarkAlpha(_look.ScaleOpacity * share, _look.ScaleStrength, pitchClass == _look.ScaleRoot, _look.ScaleEmphasiseRoot);

    /// <summary>The 1-based degree of a scale pitch class (its place counting up from the root); 0 for a note outside the scale.</summary>
    private int DegreeOf(int pitchClass)
    {
        if (!InScale(pitchClass) || _look.ScaleRoot < 0) return 0;
        var degree = 1;
        for (var pc = _look.ScaleRoot; pc != pitchClass; pc = (pc + 1) % 12) if (_look.ScalePitches[pc] == '1') degree++;
        return degree;
    }

    /// <summary>The frets that get a mark: all of them, or with "only on the frets in view" the frets the pane shows (its layout window).</summary>
    private bool FretMarked(int fret, TabForge.Presenters.FretboardGeometryRules.Layout layout) =>
        !_look.ScaleOnlyInView || (fret >= layout.FirstFret && fret <= layout.LastFret);

    /// <summary>Behind the frets: the cell tint style shades each scale note's cell, and "dim notes outside the scale" shades the other cells.</summary>
    private void DrawShadedCells(IDrawContext d, Func<string, UiColor> ink, TabForge.Presenters.FretboardGeometryRules.Layout layout)
    {
        if (!ScaleActive) return;
        var tint = _look.ScaleStyle == ScaleHighlightStyles.CellTint;
        if (!tint && !_look.ScaleDimOutside) return;
        var colour = ScaleInk(ink);
        var dim = ink("InstrumentBackground").WithOpacity(ScaleDimAlpha);
        for (var s = 0; s < layout.Strings && s < Tuning.Count; s++)
        {
            var top = TabForge.Presenters.FretboardGeometryRules.StringY(layout, s) - layout.StringGap / 2;
            for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
            {
                var pitchClass = (Tuning[s] + fret) % 12;
                var inside = _look.ScalePitches[pitchClass] == '1';
                if (inside ? !tint || !FretMarked(fret, layout) : !_look.ScaleDimOutside) continue;
                var left = TabForge.Presenters.FretboardGeometryRules.FretX(layout, fret - 0.5);
                var right = TabForge.Presenters.FretboardGeometryRules.FretX(layout, fret + 0.5);
                d.Rect(inside ? colour.WithOpacity(ScaleAlpha(pitchClass, ScaleCellShare)) : dim, null, left, top, right - left, layout.StringGap);
            }
        }
    }

    /// <summary>The dots, rings, note names and degrees styles: one mark on every fret that sounds a note of the scale.</summary>
    private void DrawScaleMarks(IDrawContext d, Func<string, UiColor> ink, TabForge.Presenters.FretboardGeometryRules.Layout layout, double radius)
    {
        if (!ScaleActive || _look.ScaleStyle == ScaleHighlightStyles.CellTint) return;
        var colour = ScaleInk(ink);
        for (var s = 0; s < layout.Strings && s < Tuning.Count; s++)
            for (var fret = layout.FirstFret; fret <= layout.LastFret; fret++)
            {
                var pitchClass = (Tuning[s] + fret) % 12;
                if (_look.ScalePitches[pitchClass] != '1' || !FretMarked(fret, layout)) continue;
                var p = TabForge.Presenters.FretboardGeometryRules.PositionOf(layout, s, fret);
                MarkScaleNote(d, colour, pitchClass, p.X, p.Y, radius);
            }
    }

    private void MarkScaleNote(IDrawContext d, UiColor colour, int pitchClass, double x, double y, double radius)
    {
        var root = pitchClass == _look.ScaleRoot && _look.ScaleEmphasiseRoot;
        var mark = colour.WithOpacity(ScaleAlpha(pitchClass));
        switch (_look.ScaleStyle)
        {
            case ScaleHighlightStyles.Rings:
                d.Ellipse(null, new UiStroke(mark, root ? ScaleRootRingWidth : ScaleRingWidth), x, y, radius, radius);
                break;
            case ScaleHighlightStyles.NoteNames:
            case ScaleHighlightStyles.Degrees:
                var label = _look.ScaleStyle == ScaleHighlightStyles.Degrees ? DegreeLabel(DegreeOf(pitchClass)) : PitchName(60 + pitchClass);
                var text = d.Text(label, Font, radius * ScaleLabelFontShare, mark, d.PixelsPerDip, weight: root ? 700 : 500);
                d.DrawText(text, x - text.Width / 2, y - text.Height / 2);
                break;
            default:
                var size = radius * (root ? ScaleRootDotShare : ScaleDotShare);
                d.Ellipse(mark, null, x, y, size, size);
                break;
        }
    }

    /// <summary>The idle fill of a key: the cell tint style tints a scale key toward the highlight colour, and "dim notes outside the scale" fades the others.</summary>
    private UiColor ScaleKeyFill(int midi, Func<string, UiColor> ink, UiColor plain)
    {
        if (!ScaleActive) return plain;
        var pitchClass = ((midi % 12) + 12) % 12;
        if (!InScale(pitchClass)) return _look.ScaleDimOutside ? ThemeColours.Blend(plain, ink("InstrumentBackground"), ScaleDimAlpha) : plain;
        if (_look.ScaleStyle != ScaleHighlightStyles.CellTint) return plain;
        return ThemeColours.Blend(plain, ScaleInk(ink), ScaleAlpha(pitchClass, ScaleCellShare));
    }

    /// <summary>The other styles put a small mark (or the note name or degree) near the bottom of each scale key, the root's stronger.</summary>
    private void ScaleKeyMark(IDrawContext d, Func<string, UiColor> ink, int midi, UiRect key)
    {
        var pitchClass = ((midi % 12) + 12) % 12;
        if (!InScale(pitchClass) || IsLit(midi) || _look.ScaleStyle == ScaleHighlightStyles.CellTint) return;
        var radius = Math.Clamp(key.Width * KeyMarkWidthShare, KeyMarkMinRadius, KeyMarkMaxRadius);
        MarkScaleNote(d, ScaleInk(ink), pitchClass, key.X + key.Width / 2, key.Bottom - KeyMarkLift, radius);
    }
}
