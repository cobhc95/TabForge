using TabForge.Models;
using TabForge.Views;

namespace TabForge;

public static partial class SelfTest
{
    // A bar of 16 sixteenth notes with palm-muted groups and a bar-start tempo stays compact.
    private static void TestSixteenthBarWidth()
    {
        double Width(bool palm, bool tempo)
        {
            var cells = Enumerable.Range(0, 16).Select(i => new TabCell
            {
                DurationDenominator = 16,
                Notes = new List<TabNote> { new() { StringIndex = 5, Fret = i % 4 == 0 ? 0 : (i % 2 == 0 ? 3 : 7), MidiValue = 40 + (i % 4 == 0 ? 0 : 3),
                    Techniques = palm && (i / 4) % 2 == 0 ? new HashSet<string> { "PalmMute" } : new HashSet<string>() } }
            }).ToList();
            var track = new TrackModel { Name = "T", Kind = TrackKind.Guitar, Measures = new List<MeasureModel> { new() { Clef = "G2", Cells = cells }, new() { Clef = "G2", Cells = Enumerable.Range(0, 16).Select(_ => new TabCell()).ToList() } } };
            var project = new SongProject { Tempo = tempo ? 120 : 0, Tracks = new List<TrackModel> { track } };
            var editor = new TabEditorControl { Project = project, SelectedTrackIndex = 0, PageWidthOverride = 1800 };
            var page = editor.Layout.GetLayout();
            var w = editor.Layout.NaturalWidths![0];
            return w;
        }
        var plain = Width(false, true);
        var full = Width(true, true);
        Check("separate palm-mute groups add under 30 px to a sixteenth-note bar (a mid-bar label reserves no grid width)", full - plain < 30, $"width {full:0.0}, plain {plain:0.0}");
    }
}
