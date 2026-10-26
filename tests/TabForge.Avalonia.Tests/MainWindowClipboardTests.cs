using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the Avalonia Edit Copy / Cut / Paste: copied beats paste back as the same notes and undo as one step, the held clipboard text is the clip's own JSON
/// (the bytes the app writes), cut clears a beat as one undo step, a whole-bar cut removes the bars (WholeBarCut).
/// Tests: MainWindowClipboard, AvaloniaScoreClipboard, ScorePreviewEditor clip calls.
/// </summary>
public class MainWindowClipboardTests
{
    private static (ScorePreviewView View, MainWindowClipboard Flow, AvaloniaScoreClipboard System) Make()
    {
        var view = new ScorePreviewView();
        var system = new AvaloniaScoreClipboard(() => null);
        var track = view.Editor.Project!.Tracks[0];
        foreach (var (cell, fret) in new[] { (0, 3), (2, 5) }) track.Measures[0].CellsForVoice(0)[cell].Notes.Add(new Models.TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret) });
        return (view, new MainWindowClipboard(view, system, () => new EditingSettings(), _ => Task.FromResult<PasteSpecialOptions?>(null)), system);
    }

    private static int[] Frets(Models.MeasureModel bar) => bar.CellsForVoice(0).SelectMany(c => c.Notes).Select(n => n.Fret).ToArray();

    [AvaloniaFact]
    public async Task CopiedBeatsPasteBackAsTheSameNotes()
    {
        var (view, flow, system) = Make();
        var editor = view.Editor;
        var song = editor.Project!;
        editor.SelectRange(0, 0, 0, 2);
        Assert.True(flow.Copy());
        var clip = flow.Clipboard.Current!;
        Assert.Equal(ScoreClipKind.Beats, clip.Kind);   // beats inside one bar copy as beats (GP5)
        Assert.Equal(clip.ToJson(), system.TryRead(InputLimits.MaxClipboardBytes).Text);   // the system clipboard carries the clip's exact JSON

        var before = Frets(song.Tracks[0].Measures[2]);
        editor.ClearSelection();
        editor.SetPosition(2, 0, 0);
        Assert.True(await flow.PasteAsync());
        Assert.Equal(new[] { 3, 5 }, Frets(song.Tracks[0].Measures[2]).Take(2));
        Assert.True(view.Run("Edit.Undo"));
        Assert.Equal(before, Frets(editor.Project!.Tracks[0].Measures[2]));   // undo restores a fresh song object
    }

    [AvaloniaFact]
    public void CutOfWholeBarsRemovesThemAsOneUndoStep()
    {
        var (view, flow, _) = Make();
        var editor = view.Editor;
        var bars = editor.Project!.Tracks[0].Measures.Count;
        editor.Project.Tracks.RemoveRange(1, editor.Project.Tracks.Count - 1);   // a one-track song: the bars go (several tracks: they are emptied)
        editor.SetPosition(1, 0, 0);
        editor.SelectRange(1, 0, 2, MusicTime.BarSlots(editor.Project, 2) - 1);   // bars 2-3 from their first beat: a one-track cut takes the bars (GP5)
        Assert.True(flow.Cut());
        Assert.NotNull(flow.Clipboard.Current);
        Assert.Equal(bars - 2, editor.Project!.Tracks[0].Measures.Count);
        Assert.Equal(1, editor.SelectedMeasure);   // the cursor stays on its bar number
        Assert.True(view.Run("Edit.Undo"));
        Assert.Equal(bars, editor.Project!.Tracks[0].Measures.Count);
    }

    [AvaloniaFact]
    public void CutClearsTheBeatAsOneUndoStep()
    {
        var (view, flow, _) = Make();
        var editor = view.Editor;
        var beat = editor.Project!.Tracks[0].Measures[0].CellsForVoice(0).First(c => c.Notes.Count > 0);
        var cell = editor.Project.Tracks[0].Measures[0].CellsForVoice(0).IndexOf(beat);
        editor.SetPosition(0, cell, 0);
        Assert.True(flow.Cut());
        Assert.DoesNotContain(editor.Project.Tracks[0].Measures[0].CellsForVoice(0), c => ReferenceEquals(c, beat) && c.Notes.Count > 0);
        Assert.Equal(0, editor.SelectedMeasure);
        Assert.True(flow.Clipboard.Current is { Kind: ScoreClipKind.Beats });
        Assert.True(view.Run("Edit.Undo"));
        Assert.Contains(editor.Project.Tracks[0].Measures[0].CellsForVoice(0), c => c.Notes.Count > 0);
    }
}
