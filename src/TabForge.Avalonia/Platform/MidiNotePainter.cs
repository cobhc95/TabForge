using TabForge.Models;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// Owns: drawing one note of the mini piano roll inside a MIDI clip box or a live MIDI take box: its row from the clip's pitch range, at least 2 px wide,
/// the velocity as opacity. The clip and the live take layer both call it, so they draw the same.
/// Does not own: the note data, which notes are visible or the time-to-x mapping (callers), or the row and opacity maths (Core <see cref="MidiNoteLayout"/>).
/// Tests: TabForge.Avalonia.Tests.MidiNoteLayoutTests, TimelineAudioClipTests.
/// </summary>
internal static class MidiNotePainter
{
    /// <summary>Draws a note from <paramref name="x1"/> to <paramref name="x2"/> in the band that starts at <paramref name="top"/> and is <paramref name="height"/> tall.</summary>
    public static void Note(IDrawContext d, UiColor colour, MidiPitchRange range, double top, double height, double x1, double x2, int pitch, int velocity)
    {
        var (y, rowHeight) = MidiNoteLayout.RowOf(pitch, range, height);
        d.Rect(colour.WithOpacity(MidiNoteLayout.OpacityOf(velocity)), null, x1, top + y, MidiNoteLayout.WidthOf(x1, x2), rowHeight);
    }
}
