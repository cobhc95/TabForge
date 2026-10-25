using System;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using TabForge.Avalonia.Platform;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fretboard's playback frames. A steady frame (the same notes, the song time moving on) allocates next to nothing, and the fade of a
/// released note repaints at most 30 times a second.
/// Does not own: the marker drawing (FretboardPlaybackMarkers), the note lists (InstrumentPlaybackLists).
/// Tests: InstrumentPanelView.ShowPlayback and Draw; InstrumentPanelView.FadeRepaintDue.
/// </summary>
public class FretboardPlaybackFrameTests
{
    // Sounding now (1000-1800), next (2000), upcoming (3000) and recently released (0-400), so a fade is drawn at song time 1200.
    private static readonly NoteEvent[] Notes =
    {
        new() { OnsetMs = 0, DurationMs = 400, StringIndex = 1, Fret = 1, Midi = 62 },
        new() { OnsetMs = 1000, DurationMs = 800, StringIndex = 0, Fret = 3, Midi = 67 },
        new() { OnsetMs = 2000, DurationMs = 300, StringIndex = 2, Fret = 2, Midi = 64 },
        new() { OnsetMs = 3000, DurationMs = 300, StringIndex = 4, Fret = 5, Midi = 69 },
    };

    private const int SteadyFrames = 300;

    // The steady frame's allocation ceiling in bytes. It guards against a return to the per-frame lists, labels and delegates (1648 bytes before
    // the change). What remains, about 230 bytes, is the movement line's path: the closure and path object that IDrawContext.Paths.BuildPath
    // makes each frame while a hand movement is drawn.
    private const double MaxBytesPerSteadyFrame = 256;

    private static InstrumentPanelView OpenPanel()
    {
        var track = DemoSongFactory.Create().Tracks[0];
        var view = new InstrumentPanelView { VerticalAlignment = VerticalAlignment.Top };
        view.Show(track, Array.Empty<InstrumentLitNotes.Lit>());
        return view;
    }

    [AvaloniaFact]
    public void SteadyPlaybackFrameAllocatesAlmostNothing()
    {
        var view = OpenPanel();
        var draw = new NullDrawContext();
        for (var warm = 0; warm < 20; warm++)
        {
            view.ShowPlayback(Notes, 1200 + warm * 0.5);
            view.Draw(draw, false);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var frame = 0; frame < SteadyFrames; frame++)
        {
            view.ShowPlayback(Notes, 1210 + frame * 0.5);
            view.Draw(draw, false);
        }
        var perFrame = (GC.GetAllocatedBytesForCurrentThread() - before) / (double)SteadyFrames;

        Assert.True(perFrame <= MaxBytesPerSteadyFrame, $"A steady playback frame allocated {perFrame:F1} bytes; the ceiling is {MaxBytesPerSteadyFrame}.");
    }

    [Fact]
    public void FadeRepaintsAtMostThirtyTimesASecond()
    {
        Assert.False(InstrumentPanelView.FadeRepaintDue(lastRepaintMs: 0, nowMs: 10));
        Assert.True(InstrumentPanelView.FadeRepaintDue(lastRepaintMs: 0, nowMs: 34));
        Assert.True(InstrumentPanelView.FadeRepaintDue(lastRepaintMs: 100, nowMs: 50), "A seek back in song time repaints the fade.");
    }
}
