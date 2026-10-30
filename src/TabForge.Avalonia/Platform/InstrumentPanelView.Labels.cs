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

// Owns: the note and fret labels a marker shows (LabelOf, the pitch-name and fret-number caches, NoteNameOf under the note names setting).
// Does not own: the marker drawing (InstrumentPanelView.cs, MarkDot and MarkFret), the tuning (the track) or the note names table (MusicTheoryService).
// Tests: TabForge.Avalonia.Tests.InstrumentPanelViewTests.
public sealed partial class InstrumentPanelView
{
    /// <summary>The label inside a marker: its note name when note names are on, else its fret number.</summary>
    private string LabelOf(NoteEvent note) => NoteNameOf(note.StringIndex, note.Fret) ?? FretLabel(note.Fret);

    // The labels a frame draws, looked up rather than built: a pitch name depends only on its MIDI pitch, and a fret number only on the fret.
    // Keys: the pitch and the fret, each 0 to 127. Ceiling: 128 entries each, filled as they are first drawn. Thread: UI thread only.
    private const int LabelCacheSize = 128;
    private readonly string?[] _pitchNames = new string?[LabelCacheSize];
    private readonly string?[] _fretLabels = new string?[LabelCacheSize];

    private string FretLabel(int fret) => fret is >= 0 and < LabelCacheSize ? _fretLabels[fret] ??= fret.ToString() : fret.ToString();

    private string PitchName(int pitch) => pitch is >= 0 and < LabelCacheSize
        ? _pitchNames[pitch] ??= MusicTheoryService.NoteName(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-')
        : MusicTheoryService.NoteName(pitch).TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '-');

    /// <summary>The label beside the nut for a string: its open note with the octave under the note names setting, else its number.</summary>
    internal string StringLabel(int stringIndex) =>
        _look.ShowNoteNames && stringIndex < Tuning.Count ? MusicTheoryService.NoteName(Tuning[stringIndex]) : (stringIndex + 1).ToString();

    /// <summary>The label the track's drum map gives a sound, shown on the key map; null for a track that is not drums (the GM name then shows).</summary>
    internal string? DrumLabelOf(int midi) => _track is { Kind: TrackKind.Drums } drums ? DrumMaps.For(drums, midi).Label : null;

    /// <summary>The dead-note mark: the ring's stroke width, the X's stroke width, and the X's arm length as a share of the note radius.</summary>
    private const double DeadRingWidth = 1.8, DeadCrossWidth = 1.6, DeadCrossArm = 0.7;

    /// <summary>The cell of a dead note: an outlined ring crossed by an X, in the cursor's colour.</summary>
    private void MarkDead(IDrawContext d, Func<string, UiColor> ink, FretboardGeometryRules.Layout layout, int stringIndex, int fret, double radius)
    {
        if (stringIndex < 0 || stringIndex >= layout.Strings) return;
        var p = FretboardGeometryRules.PositionOf(layout, stringIndex, fret);
        var colour = ink("InstrumentCurrent");
        d.Ellipse(null, new UiStroke(colour, DeadRingWidth), p.X, p.Y, radius, radius);
        var arm = radius * DeadCrossArm;
        d.Line(new UiStroke(colour, DeadCrossWidth), p.X - arm, p.Y - arm, p.X + arm, p.Y + arm);
        d.Line(new UiStroke(colour, DeadCrossWidth), p.X - arm, p.Y + arm, p.X + arm, p.Y - arm);
    }

    /// <summary>The pitch name a marker shows under the note names setting; null shows the fret number.</summary>
    private string? NoteNameOf(int stringIndex, int fret) =>
        _look.ShowNoteNames && stringIndex >= 0 && stringIndex < Tuning.Count
            ? PitchName(Tuning[stringIndex] + fret)
            : null;
}
