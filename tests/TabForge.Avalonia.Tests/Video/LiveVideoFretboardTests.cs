using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Video;
using TabForge.Playback;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services.Video;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the check that a live video frame smaller than the window (the bitmap renders at another scale) shows the same fretboard as a full-size frame, during playback.
/// Tests: AvaloniaWindowFrameCapture, FretboardArtist, AvaloniaDrawContext.ImageInside.
/// </summary>
public class LiveVideoFretboardTests
{
    private const int Width = 1400, Height = 220;

    private static byte[] Frame(int frameWidth, int frameHeight)
    {
        var view = new InstrumentPanelView();
        view.Show(DemoSongFactory.Create().Tracks[0], Array.Empty<InstrumentLitNotes.Lit>());
        view.ShowPlayback(new[] { new NoteEvent { OnsetMs = 1000, DurationMs = 800, StringIndex = 0, Fret = 3, Midi = 67 } }, 1200);
        var window = new Window { Width = Width, Height = Height, Content = view };
        window.Show();
        window.UpdateLayout();
        var capture = new AvaloniaWindowFrameCapture(frameWidth, frameHeight, 30, () => window.Content as Visual, () => new ScreenRect(0, 0, Width, Height), AvaloniaUiThread.ForApplication());
        var bgra = new byte[frameWidth * frameHeight * 4];
        capture.RenderNow();
        Assert.True(capture.Grab(bgra));
        capture.Dispose();
        window.Close();
        return bgra;
    }

    [AvaloniaFact]
    public void AShrunkFrameShowsTheSameFretboardAsAFullSizeOne()
    {
        var full = Frame(Width, Height);
        var half = Frame(Width / 2, Height / 2);
        long difference = 0, samples = 0;
        for (var y = 2; y < Height / 2 - 2; y += 2)
            for (var x = 2; x < Width / 2 - 2; x += 2)
            {
                var a = (y * Width / 2 + x) * 4;
                var total = 0;
                // Against a little square of the full frame: a shrunk wire lands on a neighbouring pixel, a missing one matches nothing.
                for (var dy = 0; dy < 2; dy++) for (var dx = 0; dx < 2; dx++) total += full[((y * 2 + dy) * Width + x * 2 + dx) * 4 + 1];
                difference += Math.Abs(half[a + 1] - total / 4);
                samples++;
            }
        Assert.InRange(difference / (double)samples, 0, 8);   // wires, strings, numbers, the legend and the notes all there
    }
}
