using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the score clipboard / cut / paste / whole-bar cut act on the SHOWN track and the active voice, not on track 1 voice 1.
/// Tests: ScorePreviewEditor CaptureClip, CutClear, Paste (ShownTrackIndex).
/// </summary>
public class ScoreClipboardTrackTests
{
    private const int Shown = 2;   // track 3

    private static (ScorePreviewView View, MainWindowClipboard Flow) Make()
    {
        var view = new ScorePreviewView();
        var project = view.Editor.Project!;
        while (project.Tracks.Count <= Shown) project.Tracks.Add(NewTrackFactory.CreateTrack(project, Models.TrackKind.Guitar));
        view.ShowTrack(Shown);
        var flow = new MainWindowClipboard(view, new AvaloniaScoreClipboard(() => null), () => new EditingSettings(), _ => Task.FromResult<PasteSpecialOptions?>(null));
        return (view, flow);
    }

    private static void AddNote(Models.TrackModel track, int voice, int cell, int fret) =>
        track.Measures[0].CellsForVoice(voice, create: true)[cell].Notes.Add(new Models.TabNote { StringIndex = 1, Fret = fret, MidiValue = track.PitchOf(1, fret) });

    private static int[] Frets(Models.TrackModel track, int bar, int voice) => track.Measures[bar].CellsForVoice(voice).SelectMany(c => c.Notes).Select(n => n.Fret).ToArray();

    [AvaloniaFact]
    public void CopyOnTrack3Voice1CapturesTrack3Voice1()
    {
        var (view, flow) = Make();
        var tracks = view.Editor.Project!.Tracks;
        AddNote(tracks[0], 0, 0, 3);
        AddNote(tracks[Shown], 0, 0, 7);
        view.Editor.SetPosition(0, 0, 0);
        Assert.True(flow.Copy());
        Assert.Contains("\"fret\":7", flow.Clipboard.Current!.ToJson().Replace(" ", "").ToLowerInvariant());
        Assert.DoesNotContain("\"fret\":3", flow.Clipboard.Current.ToJson().Replace(" ", "").ToLowerInvariant());
    }

    [AvaloniaFact]
    public void Voice2CopyCapturesVoice2()
    {
        var (view, flow) = Make();
        var track = view.Editor.Project!.Tracks[Shown];
        AddNote(track, 0, 0, 3);
        AddNote(track, 1, 0, 9);
        Assert.True(view.Editor.RunPaletteTool("gp:voice_2"));
        view.Editor.SetPosition(0, 0, 0);
        Assert.True(flow.Copy());
        var json = flow.Clipboard.Current!.ToJson().Replace(" ", "").ToLowerInvariant();
        Assert.Contains("\"fret\":9", json);
        Assert.DoesNotContain("\"fret\":3", json);
    }

    [AvaloniaFact]
    public void CutOnTrack3RemovesFromTrack3NotTrack1()
    {
        var (view, flow) = Make();
        var tracks = view.Editor.Project!.Tracks;
        AddNote(tracks[0], 0, 0, 3);
        AddNote(tracks[Shown], 0, 0, 7);
        view.Editor.SetPosition(0, 0, 0);
        Assert.True(flow.Cut());
        tracks = view.Editor.Project!.Tracks;
        Assert.Equal(new[] { 3 }, Frets(tracks[0], 0, 0));
        Assert.Empty(Frets(tracks[Shown], 0, 0));
    }

    [AvaloniaFact]
    public async Task PasteLandsOnShownTrackAndVoice()
    {
        var (view, flow) = Make();
        var track = view.Editor.Project!.Tracks[Shown];
        AddNote(track, 1, 0, 9);
        Assert.True(view.Editor.RunPaletteTool("gp:voice_2"));
        view.Editor.SetPosition(0, 0, 0);
        Assert.True(flow.Copy());
        view.Editor.SetPosition(1, 0, 0);
        Assert.True(await flow.PasteAsync());
        var tracks = view.Editor.Project!.Tracks;
        Assert.Equal(new[] { 9 }, Frets(tracks[Shown], 1, 1));
        Assert.Empty(Frets(tracks[0], 1, 0));
        Assert.Empty(Frets(tracks[Shown], 1, 0));
    }

    [AvaloniaFact]
    public void WholeBarCutUsesShownTrack()
    {
        var (view, flow) = Make();
        var editor = view.Editor;
        var tracks = editor.Project!.Tracks;
        var bars = tracks[0].Measures.Count;
        AddNote(tracks[0], 0, 0, 3);
        AddNote(tracks[Shown], 0, 0, 7);
        editor.SelectRange(0, 0, 0, MusicTime.BarSlots(editor.Project, 0) - 1);
        Assert.True(flow.Cut());
        tracks = editor.Project!.Tracks;
        Assert.Equal(bars, tracks[0].Measures.Count);   // several tracks: the bar is emptied, not removed
        Assert.Equal(new[] { 3 }, Frets(tracks[0], 0, 0));
        Assert.Empty(Frets(tracks[Shown], 0, 0));
    }
}
