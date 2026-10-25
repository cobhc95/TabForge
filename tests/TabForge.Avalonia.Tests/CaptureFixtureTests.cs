using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TabForge.Avalonia.Capture;
using TabForge.Avalonia.Views;
using TabForge.Capture;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the capture harness additions: shot names that stay under the output folder, the made-up WAV and REAPER chain, the GIF LZW code-size boundary read by an independent decoder,
/// and the fixture, pointer and live-strip verbs run in a session.
/// Does not own: the rest of the harness (CaptureSessionTests, CaptureScriptTests, GifEncoderTests).
/// Tests: this class.
/// </summary>
public class CaptureFixtureTests
{
    private static string NewFolder() => DialogTestKit.RenderDirectory(nameof(CaptureFixtureTests) + Guid.NewGuid().ToString("N"));

    [Theory]
    [InlineData("../x"), InlineData("a/../../x"), InlineData("/x"), InlineData("C:/x"), InlineData("a\b"), InlineData(""), InlineData("a//b")]
    public void ShotNamesThatLeaveTheOutputFolderAreRefused(string name) => Assert.Throws<ArgumentException>(() => CaptureNames.PathUnder(NewFolder(), name, ".png"));

    [Fact]
    public void ShotNamesWithFoldersStayUnderTheOutputFolder()
    {
        var folder = NewFolder();
        Assert.StartsWith(Path.GetFullPath(folder), CaptureNames.PathUnder(folder, "docs/screenshots/a-b", ".png"));
    }

    [Fact]
    public void TheFixtureFolderIsApprovedSoTheAppReadsItsPeaks()
    {
        TabForge.Avalonia.Program.InstallMediaPathPolicy();
        var file = CaptureFixtures.WriteWave(NewFolder(), "tone", 1);
        var media = new TabForge.Services.MediaContext(() => null);
        TabForge.Avalonia.Capture.FixtureSteps.ApproveFolder(file, media);
        Assert.True(TabForge.Services.MediaAccess.Decide(TabForge.Services.MediaPathPolicy.Classify(file, media.BaseDirectory), media).Allowed);
    }

    [Fact]
    public void TheMadeUpWaveIsAReadableMonoWavOfTheAskedLength()
    {
        var path = CaptureFixtures.WriteWave(NewFolder(), "tone", 2.5);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(CaptureFixtures.SampleRate, BitConverter.ToInt32(bytes, 24));
        Assert.Equal((int)(2.5 * CaptureFixtures.SampleRate) * 2, BitConverter.ToInt32(bytes, 40));
        Assert.Contains(bytes.Skip(44), b => b != 0);
    }

    [Fact]
    public void TheMadeUpReaperChainHasFourPlugInsAndOneContainerOfTwo()
    {
        var parsed = ReaperChainImporter.Parse(CaptureFixtures.ReaperChain);
        Assert.Equal(4, parsed.Entries.Count);
        var group = Assert.Single(parsed.Groups);
        Assert.Equal((0, 2), (group.Start, group.Count));
        Assert.False(parsed.Entries[3].Enabled);
    }

    [AvaloniaFact]
    public void LzwCodeSizeGrowsAtTheRightCodeSoAnIndependentDecoderReadsEveryPixel()
    {
        const int w = 160, h = 120;
        var rng = new Random(11);
        var rgba = new byte[w * h * 4];
        for (var i = 0; i < w * h; i++)
        {
            rgba[i * 4] = (byte)(rng.Next(5) * 60); rgba[i * 4 + 1] = (byte)(rng.Next(5) * 60); rgba[i * 4 + 2] = (byte)(rng.Next(4) * 80); rgba[i * 4 + 3] = 255;
        }
        using var gif = new MemoryStream();
        GifEncoder.Write(gif, new[] { new GifFrame(w, h, rgba, 100) });
        gif.Position = 0;
        using var decoded = new Bitmap(gif);
        using var copy = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96), PixelFormat.Rgba8888, AlphaFormat.Unpremul);
        using (var locked = copy.Lock()) decoded.CopyPixels(locked, AlphaFormat.Unpremul);
        var back = new byte[w * h * 4];
        using (var locked = copy.Lock())
            for (var y = 0; y < h; y++) System.Runtime.InteropServices.Marshal.Copy(locked.Address + y * locked.RowBytes, back, y * w * 4, w * 4);
        for (var i = 0; i < w * h; i++)
            for (var c = 0; c < 3; c++) Assert.True(Math.Abs(rgba[i * 4 + c] - back[i * 4 + c]) <= 1, $"pixel {i} channel {c}");
    }

    [AvaloniaFact]
    public async Task TheRepeatFixtureClipsGetTheirPeaksFromTheApp()
    {
        TabForge.Avalonia.Program.InstallMediaPathPolicy();
        var session = new CaptureSession(NewFolder());
        await session.StartAsync();
        await session.RunAsync(CaptureScript.Parse("""[ {"audio-fixture":"repeat"}, {"wait":1500} ]"""), "peaks");
        var clip = session.Document.Project.Tracks.Last().AudioClips[0];
        var status = TabForge.Audio.WaveformCache.Default.StatusOf(clip.File, session.Document.Media);
        var peaks = TabForge.Audio.WaveformCache.Default.Get(clip.File, session.Document.Media);
        session.Window.Close();
        Assert.True(peaks is { Length: > 0 }, status.State + " " + status.Message);
    }

    [AvaloniaFact]
    public async Task FixturePointerAndLiveStripVerbsRunAndAnUnsafeShotNameFailsOnlyItsStep()
    {
        TabForge.Avalonia.Program.InstallMediaPathPolicy();
        var folder = NewFolder();
        var session = new CaptureSession(folder);
        await session.StartAsync();
        var failed = await session.RunAsync(CaptureScript.Parse("""
            [ {"audio-fixture":"end"}, {"pointer":"press","clip":[10,0]}, {"pointer":"move","by-bars":9}, {"shot":"mid","target":"timeline","scale":0.5}, {"pointer":"release"},
              {"live-fx":"open"}, {"live-fx":"readings","in":0.5,"out":0.4,"hz":110.0,"repeat":3}, {"shot":"strip","target":"window:LiveInputFx","scale":0.5},
              {"fx-chain":"reaper"}, {"shot":"chain","target":"window:FxChain","scale":0.5}, {"close":"all"}, {"shot":"../escape"} ]
            """), "fixtures");
        var clip = session.Document.Project.Tracks[10].AudioClips[0];
        var ghosts = session.Find<ArrangementView>().Timeline.GhostBars;
        session.Window.Close();
        Assert.Equal(1, failed);
        Assert.Equal(0, ghosts);   // the release cleared the preview
        Assert.True(clip.EndSec > 0);
        foreach (var name in new[] { "mid", "strip", "chain" }) Assert.True(File.Exists(Path.Combine(folder, name + ".png")), name);
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(folder.TrimEnd(Path.DirectorySeparatorChar))!, "escape.png")));
    }
}
