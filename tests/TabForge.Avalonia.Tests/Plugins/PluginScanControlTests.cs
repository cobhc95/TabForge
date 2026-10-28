using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views.Fx;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: Pause, Resume and Stop of the Add plug-in window's identify pass, and continuing it after the window is reopened.
/// Does not own: the identify work itself (FxAddPluginTests) or the buttons' drawing.
/// Tests: this class.
/// </summary>
public class PluginScanControlTests
{
    private const int Count = 24;

    private sealed class PathTrust : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => fullPath;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    private static (PluginSettings Settings, PluginTrust Trust, TabForge.Platform.IPluginFileTrust Files) Setup()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-scan-control", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var files = new PathTrust();
        var trust = new PluginTrust();
        var settings = new PluginSettings();
        var list = new List<VstPluginInfo>();
        for (var i = 0; i < Count; i++)
        {
            var file = Path.Combine(folder, $"P{i}.dll");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, (byte)i });
            trust.Approve(settings, file, files);
            list.Add(new VstPluginInfo($"P{i}", file, "VST2", "", ""));
        }
        settings.LastScan = list;
        return (settings, trust, files);
    }

    private static async Task WaitFor(Func<bool> done)
    {
        for (var i = 0; i < 200 && !done(); i++) await Task.Delay(25);
    }

    [AvaloniaFact]
    public async Task PauseHoldsBetweenPluginsAndResumeFinishes()
    {
        var (settings, trust, files) = Setup();
        var probed = 0;
        PluginCatalog.ProbeOverride = (_, _, _) => { Interlocked.Increment(ref probed); Thread.Sleep(20); return ("Effect", "V", "N"); };
        try
        {
            var session = new PluginBrowserSession(settings, trust, files, () => { });
            var texts = new List<string>();
            session.Status += texts.Add;
            var run = session.IdentifyAsync(() => true);
            Assert.Equal(IdentifyRun.Running, session.Run);
            session.Pause();
            Assert.Equal(IdentifyRun.Paused, session.Run);
            await Task.Delay(400);
            var heldAt = probed;
            Assert.InRange(heldAt, 0, Count - 1);   // some probes were already running; the rest wait
            await Task.Delay(200);
            Assert.Equal(heldAt, probed);           // nothing new starts while paused
            Assert.StartsWith("Paused at ", texts[0]);
            session.Resume();
            await run;
            Assert.Equal(Count, probed);
            Assert.Equal(Count, settings.Probed.Count);
            Assert.Equal(IdentifyRun.Idle, session.Run);
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public async Task StopEndsThePassAndReopeningContinuesIt()
    {
        var (settings, trust, files) = Setup();
        PluginCatalog.ProbeOverride = (_, _, _) => { Thread.Sleep(20); return ("Effect", "V", "N"); };
        try
        {
            var session = new PluginBrowserSession(settings, trust, files, () => { });
            var texts = new List<string>();
            session.Status += texts.Add;
            var run = session.IdentifyAsync(() => true);
            session.Pause();
            await Task.Delay(300);
            session.Stop();
            await run;
            Assert.Equal(IdentifyRun.Stopped, session.Run);
            var (done, total) = session.IdentifyProgress;
            Assert.Equal(Count, total);
            Assert.True(done < Count && settings.Probed.Count == done);
            Assert.Contains($"Stopped at {done} of {Count} · {Count - done} not checked yet", texts[^1]);
            Assert.Contains("Rescan to finish.", texts[^1]);

            // The window is opened again: the plug-ins already identified are kept, the pass continues with the rest.
            var reopened = new PluginBrowserSession(settings, trust, files, () => { });
            await reopened.IdentifyAsync(() => true);
            Assert.Equal(Count, settings.Probed.Count);
            Assert.Equal(Count, reopened.IdentifyProgress.Done);
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    [AvaloniaFact]
    public void PauseStopAndResumeDoNothingWhenNoPassRuns()
    {
        var session = new PluginBrowserSession(new PluginSettings(), new PluginTrust(), new PathTrust(), () => { });
        session.Pause(); session.Resume(); session.Stop();
        Assert.Equal(IdentifyRun.Idle, session.Run);
    }
}
