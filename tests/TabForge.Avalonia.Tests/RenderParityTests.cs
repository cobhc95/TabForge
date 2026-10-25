using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using TabForge.Audio;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Rendering;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: render parity between the apps: the demo song rendered through the Avalonia Render path (AvaloniaRenderRunner's request, the shared
/// Core RenderJob, the real engine on the Null driver) writes the same WAV bytes as the WPF render path. The WPF side is the WPF build's
/// <c>--audio-audit</c> (File > Render's own job, same settings: 32-bit float, 4 s tail, master 100, a stem per track); point
/// TABFORGE_WPF_RENDER_DIR at its output folder to compare bytes, else only the Avalonia render's own checks run.
/// Tests: Views/AvaloniaRenderRunner, Core/Rendering/RenderJob.
/// </summary>
public class RenderParityTests
{
    private const string WpfRenderDirVariable = "TABFORGE_WPF_RENDER_DIR";
    private const string MixFile = "mix.wav";
    private const int TailMs = 4000;   // AudioAudit's tail
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromMinutes(10);

    private readonly ITestOutputHelper _out;
    public RenderParityTests(ITestOutputHelper output) { _out = output; AlphaTabWarmUp.EnsureReady(); }

    [Fact]
    public async System.Threading.Tasks.Task TheAvaloniaRenderPathWritesTheWpfRenderBytes()
    {
        var songPath = TestRepo.DemoSong();
        Assert.True(File.Exists(songPath), "the demo song is in the checkout root (set TABFORGE_REPO to the checkout)");
        var project = GuitarProImporter.Import(songPath);
        var outDir = Path.Combine(Path.GetTempPath(), "tf-render-parity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outDir);
        using var client = new AudioEngineClient(new TabForge.Plugins.PluginTrust()) { EngineExeForTest = Path.Combine(AppContext.BaseDirectory, "TabForge.exe"), WarmIdle = TimeSpan.Zero };
        var plugins = new PluginSettings { Driver = "Null", Device = "", SampleRate = 48000 };
        try
        {
            var timeline = RenderSpecBuilder.Compile(project);
            var (startMs, endMs) = RenderSpecBuilder.Bounds(timeline, RenderBounds.Song, 0, 0, 0, 0, 0, 0);
            var stems = project.Tracks.Select((t, i) => (t, i)).ToDictionary(x => x.t, x => Path.Combine(outDir, $"{x.i + 1:00} {Sanitize(x.t.Name)}.wav"));
            var plan = new RenderPlan(timeline, startMs, endMs, Path.Combine(outDir, MixFile), stems);
            var settings = new RenderSettings { Format = 2, TailMode = 1, TailMs = TailMs, SampleRate = 0, Mono = false, SafetyLimiter = true };
            var request = AvaloniaRenderRunner.Request(project, plan, settings, plugins, 100, restore: null, confirmIncomplete: _ => System.Threading.Tasks.Task.FromResult(true));
            var job = RenderJob.RunAsync(request, null, CancellationToken.None, client);
            Assert.Same(job, await System.Threading.Tasks.Task.WhenAny(job, System.Threading.Tasks.Task.Delay(RenderTimeout)));   // else the render did not finish in time
            await job;

            var files = Directory.GetFiles(outDir, "*.wav").Select(Path.GetFileName).OrderBy(n => n).ToList();
            Assert.Contains(MixFile, files);
            Assert.Equal(project.Tracks.Count + 1, files.Count);
            if (Environment.GetEnvironmentVariable(WpfRenderDirVariable) is not { Length: > 0 } wpfDir) return;
            foreach (var name in files)
            {
                var ours = Hash(Path.Combine(outDir, name!)); var theirs = Hash(Path.Combine(wpfDir, name!));
                _out.WriteLine($"{name}: {ours} / {theirs}");
                Assert.Equal(theirs, ours);
            }
        }
        finally
        {
            try { client.Sync(Array.Empty<TrackModel>(), plugins); } catch (InvalidOperationException) { }   // WarmIdle zero: stops the engine
            try { Directory.Delete(outDir, recursive: true); } catch (IOException) { }
        }
    }

    private static string Hash(string path) { using var stream = File.OpenRead(path); return Convert.ToHexString(SHA256.HashData(stream)); }

    // AudioAudit's file-name rule, so the stems line up with the WPF output.
    private static string Sanitize(string name)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(c => bad.Contains(c) ? '_' : c).ToArray()).Trim();
        return clean.Length == 0 ? "Track" : clean.Length > 60 ? clean[..60] : clean;
    }
}
