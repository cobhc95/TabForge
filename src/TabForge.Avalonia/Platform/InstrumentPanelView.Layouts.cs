using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

// Owns: the keyboard and the drum key-map layouts of the panel (DrawKeyboard, DrawDrums).
// Does not own: the fretboard layout (InstrumentPanelView.cs), the palette (AvaloniaPalette) or the key rules (KeyboardGeometryRules, PercussionKeyMapRules).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public sealed partial class InstrumentPanelView
{
    /// <summary>The keys' labels: their size, and the narrowest key that carries a name for every key under the note names setting.</summary>
    private const double KeyNameFontSize = 9, KeyNameMinWidth = 12;
    /// <summary>The scale marks on the keys (circles and rings): the radius as a share of the key's width, its bounds, and its distance up from the bottom.</summary>
    private const double KeyMarkWidthShare = 0.28, KeyMarkMinRadius = 2.5, KeyMarkMaxRadius = 7, KeyMarkLift = 30;

    private void DrawKeyboard(IDrawContext d, Func<string, UiColor> ink)
    {
        var area = KeysArea();
        var (lowest, highest) = KeyRange();
        var keyWidth = area.Width / Math.Max(1, KeyboardGeometryRules.WhiteKeysIn(lowest, highest));
        var edge = new UiStroke(ink("InstrumentBoardEdge"), 1);
        var white = ink(_look.WhiteKeyKey);
        var whiteIndex = 0;
        for (var midi = lowest; midi <= highest; midi++)
        {
            if (!KeyboardGeometryRules.IsWhite(midi)) continue;
            var r = KeyboardGeometryRules.WhiteKeyRect(area, keyWidth, whiteIndex++);
            var litFill = LitFill(midi, ink);
            d.Rect(litFill ?? ScaleKeyFill(midi, ink, white), edge, r.X, r.Y, r.Width, r.Height);
            if (litFill is null && KeyShade(midi, ink) is { } shade) d.Rect(shade.WithOpacity(HoverShade.OverlayOpacity), null, r.X, r.Y, r.Width, r.Height);
            ScaleKeyMark(d, ink, midi, r);
            if (midi % 12 != 0 && !(_look.ShowNoteNames && keyWidth >= KeyNameMinWidth)) continue;
            var label = d.Text(KeyName(midi), Font, KeyNameFontSize, ink("KeyboardKeyLabel"), d.PixelsPerDip);
            d.DrawText(label, r.X + r.Width / 2 - label.Width / 2, r.Bottom - 16);
        }
        whiteIndex = 0;
        for (var midi = lowest; midi <= highest; midi++)
        {
            if (KeyboardGeometryRules.IsWhite(midi)) { whiteIndex++; continue; }
            var r = KeyboardGeometryRules.BlackKeyRect(area, keyWidth, whiteIndex);
            var litBlack = LitFill(midi, ink);
            d.Rect(litBlack ?? ScaleKeyFill(midi, ink, ink("KeyboardBlackKey")), new UiStroke(ink("InstrumentBackground"), 1), r.X, r.Y, r.Width, r.Height);
            if (litBlack is null && KeyShade(midi, ink) is { } shade) d.Rect(shade.WithOpacity(HoverShade.OverlayOpacity), null, r.X, r.Y, r.Width, r.Height);
        }
    }

    /// <summary>A key's label: C keys carry their octave; with the note names setting every key carries its name, when the key is wide enough.</summary>
    private static string KeyName(int midi) =>
        midi % 12 == 0 ? MusicTheoryService.NoteName(midi) : MusicTheoryService.NoteNames[midi % 12];

    /// <summary>The idle fill of a key: under the shaded scale style a scale key is tinted toward the highlight colour, its root more strongly (as WPF).</summary>
    private UiColor ScaleKeyFill(int midi, Func<string, UiColor> ink, UiColor plain)
    {
        var pitchClass = ((midi % 12) + 12) % 12;
        if (_look.ScaleStyle != ScaleHighlightStyles.Shaded || _look.ScalePitches.Length != 12 || _look.ScalePitches[pitchClass] != '1') return plain;
        var white = KeyboardGeometryRules.IsWhite(midi);
        var root = pitchClass == _look.ScaleRoot;
        var strength = _look.ScaleStrength;
        var amount = white
            ? (root ? Math.Min(1, 0.62 * strength) : Math.Min(0.9, 0.35 * strength))
            : (root ? Math.Min(1, 0.8 * strength) : Math.Min(0.9, 0.5 * strength));
        return ThemeColours.Blend(plain, ink(_look.ScaleColourKey), amount);
    }

    /// <summary>The circles and rings styles put a small mark near the bottom of each scale key, the root's larger (as WPF).</summary>
    private void ScaleKeyMark(IDrawContext d, Func<string, UiColor> ink, int midi, UiRect key)
    {
        var pitchClass = ((midi % 12) + 12) % 12;
        if (_look.ScalePitches.Length != 12 || _look.ScalePitches[pitchClass] != '1' || IsLit(midi)) return;
        if (_look.ScaleStyle is not (ScaleHighlightStyles.Circles or ScaleHighlightStyles.Rings)) return;
        var root = pitchClass == _look.ScaleRoot;
        var radius = Math.Clamp(key.Width * KeyMarkWidthShare, KeyMarkMinRadius, KeyMarkMaxRadius);
        var cx = key.X + key.Width / 2;
        var cy = key.Bottom - KeyMarkLift;
        var colour = ink(_look.ScaleColourKey);
        if (_look.ScaleStyle == ScaleHighlightStyles.Rings) d.Ellipse(null, new UiStroke(colour, root ? 2.2 : 1.5), cx, cy, radius, radius);
        else d.Ellipse(colour, null, cx, cy, radius, radius);
    }

    /// <summary>The drum key map, as the WPF panel draws it: every GM sound labelled "N - Name" in columns, the lit ones glowing in the current colour.</summary>
    private void DrawDrums(IDrawContext d, Func<string, UiColor> ink)
    {
        var grid = PercussionKeyMapRules.GridFor(Bounds.Width, Bounds.Height);
        var fontSize = PercussionKeyMapRules.LabelFontSize(grid);
        for (var i = 0; i < PercussionKeyMapRules.Names.Count; i++)
        {
            var rect = PercussionKeyMapRules.CellRect(grid, i);
            var midi = PercussionKeyMapRules.FirstNote + i;
            var fill = LitFill(midi, ink);
            var lit = fill is not null;
            if (KeyShade(midi, ink) is { } shade) d.Rect(shade, null, rect.X, rect.Y, rect.Width, rect.Height, 3);
            if (fill is { } f) d.Rect(f.WithOpacity(0.8), new UiStroke(f.WithOpacity(0.9), 1), rect.X, rect.Y, rect.Width, rect.Height, 3);
            var label = d.Text(PercussionKeyMapRules.Label(midi, DrumLabelOf(midi)), Font, fontSize, ink(lit ? "InstrumentText" : "InstrumentMuted"), d.PixelsPerDip, weight: lit ? 700 : 400);
            d.DrawText(label, rect.X + 4, rect.Y + (grid.CellHeight - fontSize * 1.35) / 2);
        }
    }
}
