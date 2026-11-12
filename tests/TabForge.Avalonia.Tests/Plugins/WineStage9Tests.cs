// Owns: Stage 9 proof on Windows (no Wine/Linux here): the six audit fixes plus refusal categories,
// bridged timeout, stale shm sweep, Linux crash-dump mapping and first-run status texts.
// Does not own: real Wine runs (wine-stage9.yml gate on Linux).
// Tests: this class. Shares PluginCatalog.ProbeOverride, so it runs in the "PluginProbeSeam" collection.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TabForge.AudioEngine.Isolation;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

[Collection("PluginProbeSeam")]
public sealed class WineStage9Tests
{
    private sealed class PathTrust : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => fullPath;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    private static (PluginSettings Settings, PluginTrust Trust, IPluginFileTrust Files, List<VstPluginInfo> Plugins) Setup(int count)
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-stage9", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var plugins = new List<VstPluginInfo>();
        for (var i = 0; i < count; i++)
        {
            var file = Path.Combine(folder, $"S{i}.dll");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3, (byte)i });
            plugins.Add(new VstPluginInfo($"S{i}", file, "VST2", "", ""));
        }
        return (new PluginSettings { LastScan = plugins }, new PluginTrust(), new PathTrust(), plugins);
    }

    private static List<PluginBrowserRow> Rows(IEnumerable<VstPluginInfo> plugins) =>
        plugins.Select(p => new PluginBrowserRow(p.Name, p.Vendor, p.Format, p.Role, p)).ToList();

    // P1-01: a non-covered exception type (Win32Exception) in one plug-in never aborts Approve all.
    [Fact]
    public async Task ApproveAllContinuesPastANonCoveredException()
    {
        var (settings, trust, files, plugins) = Setup(3);
        PluginCatalog.ProbeOverride = (path, _, _) =>
            path == plugins[1].Path ? throw new System.ComponentModel.Win32Exception("locked") : ("Effect", "V", "N");
        try
        {
            var steps = new List<PluginApprovalStep>();
            var done = await PluginApprovalPresenter.RunAsync(plugins, settings, trust, files, steps.Add, CancellationToken.None);
            Assert.Equal(3, done);
            Assert.Equal(3, steps.Count);
            Assert.True(steps[0].Identified);
            Assert.False(steps[1].Identified);
            Assert.True(steps[2].Identified);
            Assert.NotNull(steps[1].Refusal);
        }
        finally { PluginCatalog.ProbeOverride = null; }
    }

    // P1-03: one shared probe ceiling; bridged ceiling is longer for first Wine setup.
    [Fact]
    public void ProbeTimeoutsAreSharedAndBridgedIsLonger()
    {
        Assert.Equal(PluginCatalog.DefaultProbeTimeoutMs, PluginApprovalPresenter.ProbeTimeoutMs);
        Assert.True(PluginCatalog.BridgedProbeTimeoutMs > PluginCatalog.DefaultProbeTimeoutMs);
        Assert.True(PluginCatalog.BridgedProbeTimeoutMs >= 120000);
        Assert.True(PluginInfoProbe.BridgeTimeoutForCurrentOs >= PluginInfoProbe.BridgeProbeTimeoutMs);
    }

    // P2-05: path keys use the OS comparer; Linux is case-sensitive.
    [Fact]
    public void PathKeysUseTheOsComparer()
    {
        Assert.False(PathComparer.For(HostOs.Linux).Equals("/plug/Foo.vst3", "/plug/foo.vst3"));
        Assert.True(PathComparer.For(HostOs.Windows).Equals(@"C:\plug\Foo.vst3", @"c:\plug\foo.vst3"));
        // The session sets use the OS default (P2-05), not a hardcoded ignore-case.
        var session = new TabForge.Avalonia.Views.Fx.PluginBrowserSession(new PluginSettings(), new PluginTrust(), new PathTrust(), () => { });
        Assert.Same(PathComparer.Default, session.CouldNotIdentify.Comparer);
        Assert.Same(PathComparer.Default, session.NotIdentified.Comparer);
        // Case-pair dedup follows the OS comparer.
        var a = new VstPluginInfo("A", "/plug/Foo.vst3", "VST3", "", "");
        var b = new VstPluginInfo("B", "/plug/foo.vst3", "VST3", "", "");
        var merged = PluginBrowserListPresenter.MergeCommonScan(new[] { a }, new[] { b });
        Assert.Equal(OperatingSystem.IsLinux() ? 2 : 1, merged.Count);
    }

    // P2-01: off-Windows ASIO/volume paths are stubs (no MMDevice/AsioOut type load); the ASIO fallback answers empty.
    [Fact]
    public void WindowsAudioIsolationHasNoOpStubs()
    {
        var answer = TabForge.AudioEngine.Output.AsioProbe.Answer(9, "Gone",
            () => null, n => n,
            () => throw new InvalidOperationException("ASIO is Windows-only."),
            _ => throw new InvalidOperationException("ASIO is Windows-only."),
            new Dictionary<string, TabForge.AudioEngine.Output.AsioOffer>(), new TabForge.AudioEngine.EngineLogWriter());
        Assert.Equal(9, answer.RequestId);
        Assert.Empty(answer.Rates);
    }

    // P2-07: the VST3 shim name follows the OS.
    [Fact]
    public void ShimFileNameFollowsTheOs()
    {
        var expected = OperatingSystem.IsWindows() ? "tfvst3.dll" : OperatingSystem.IsMacOS() ? "libtfvst3.dylib" : "libtfvst3.so";
        Assert.Equal(expected, TabForge.AudioEngine.EngineSession.ShimFileName);
        var spec = new TabForge.Audio.Contracts.PluginSpec("/plug/X.vst3", "VST3", false, true, 100, null, "X");
        var text = TabForge.AudioEngine.EngineSession.LoadFailureText(spec, new DllNotFoundException("tfvst3"));
        Assert.Contains(expected, text);
    }

    // P1-06: a truncated state payload is refused, not silently applied.
    [Fact]
    public void ShortStateReadsAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => PluginHostMain.ValidateStatePayload(10, new byte[5]));
        PluginHostMain.ValidateStatePayload(4, new byte[4]);
    }

    // Refusal categories are readable per helper output.
    [Fact]
    public void RefusalsAreReadablePerCategory()
    {
        Assert.Contains("iLok", ProbeRefusal.Classify("the plug-in could not be loaded (error 126). It may need PACE iLok driver", 1, underWine: true));
        Assert.Contains("missing", ProbeRefusal.Classify("the plug-in could not be loaded (error 126). It may need a DLL that is missing", 1, underWine: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("bitness", ProbeRefusal.Classify("The VST3 bundle has no 64-bit Windows binary (Contents\\x86_64-win).", 1, underWine: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("helper crash", ProbeRefusal.Classify("the plug-in faulted while starting", 1, underWine: true), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("did not answer", ProbeRefusal.ForTimeout(180000), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("prefix", ProbeRefusal.BrokenPrefixText, StringComparison.OrdinalIgnoreCase);
    }

    // A native probe (Windows) keeps its own reason: no Wine or Linux wording for a plug-in that never went near Wine.
    [Theory]
    [InlineData("The plug-in could not be loaded: a file it needs is missing (Windows error 126).")]
    [InlineData("The plug-in could not be loaded: access denied (Windows error 5).")]
    [InlineData("This plug-in needs the iLok driver to run.")]
    [InlineData("'x.dll' could not be loaded (bad image). It may be built for another system or CPU.")]
    public void NativeProbeFailuresNeverMentionWine(string stderr)
    {
        var text = ProbeRefusal.Classify(stderr, 1, underWine: false);
        Assert.Equal(stderr, text);
        Assert.DoesNotContain("Wine", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Linux", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NativeProbeRefusalAndCrashStayReadable()
    {
        Assert.StartsWith("The plug-in was refused before loading:", ProbeRefusal.Classify("hash mismatch", 5, underWine: false));
        Assert.Contains("helper crash", ProbeRefusal.Classify("", -1073741819, underWine: false), StringComparison.OrdinalIgnoreCase);
    }

    // Bridged probe detection prefers the saved label (no file IO) and upgrades the timeout.
    [Fact]
    public void BridgedFormatNeedsTheLongCeiling()
    {
        Assert.True(PluginCatalog.NeedsBridgedProbe("/nonexistent/X.dll", PluginFormats.Bridged32));
        Assert.True(PluginCatalog.NeedsBridgedProbe("/nonexistent/X.dll", PluginFormats.Bridged64));
        Assert.False(PluginCatalog.NeedsBridgedProbe("/nonexistent/X.clap", PluginFormats.Clap));
    }

    // Stale tf-wine-*.shm sweep deletes only old files.
    [Fact]
    public void StaleShmSweepDeletesOnlyOldFiles()
    {
        var folder = Path.Combine(Path.GetTempPath(), "tf-shm-sweep-" + Guid.NewGuid().ToString("N"), "tabforge-shm");
        Directory.CreateDirectory(folder);
        var oldFile = Path.Combine(folder, "tf-wine-old.shm");
        var newFile = Path.Combine(folder, "tf-wine-new.shm");
        var other = Path.Combine(folder, "other.txt");
        File.WriteAllBytes(oldFile, new byte[16]);
        File.WriteAllBytes(newFile, new byte[16]);
        File.WriteAllText(other, "x");
        File.SetLastWriteTimeUtc(oldFile, DateTime.UtcNow - TimeSpan.FromHours(5));
        var deleted = WineRuntime.SweepStaleBlocks(TimeSpan.FromHours(2), folder);
        Assert.Equal(1, deleted);
        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.True(File.Exists(other));
        try { Directory.Delete(Path.GetDirectoryName(folder)!, true); } catch (IOException) { }
    }

    // Linux crash-dump mapping finds the Wine prefix layout.
    [Fact]
    public void LinuxCrashDumpMapsThePrefixLayout()
    {
        var prefix = Path.Combine(Path.GetTempPath(), "tf-prefix-" + Guid.NewGuid().ToString("N"));
        var dir = Path.Combine(prefix, "drive_c", "users", "tester", "AppData", "Local", "TabForge", "Diagnostics");
        Directory.CreateDirectory(dir);
        var dump = Path.Combine(dir, "tfvst32-1234.dmp");
        File.WriteAllText(dump, "dump");
        Assert.Equal(dump, WineRuntime.LinuxCrashDump(prefix, "tfvst32", 1234));
        Assert.Null(WineRuntime.LinuxCrashDump(prefix, "tfvst32", 9999));
        try { Directory.Delete(prefix, true); } catch (IOException) { }
    }

    // First-run status texts are shared by the browser and the FX window.
    [Fact]
    public void FirstRunStatusIsVisible()
    {
        Assert.Contains("Wine", PluginBrowserListPresenter.WinePreparingText);
        var bridged = new List<VstPluginInfo> { new("X", "/x.dll", PluginFormats.Bridged32, "", "") };
        var native = new List<VstPluginInfo> { new("Y", "/y.vst3", "VST3", "", "") };
        // On Windows no preparing preamble (no Wine); on Linux bridged rows prepare.
        var text = PluginBrowserListPresenter.WinePreparingTextFor(bridged);
        if (OperatingSystem.IsLinux()) Assert.Equal(PluginBrowserListPresenter.WinePreparingText, text);
        else Assert.Equal(PluginApprovalPresenter.RunningText(0, 1), text);
        Assert.Equal(PluginApprovalPresenter.RunningText(0, 1), PluginBrowserListPresenter.WinePreparingTextFor(native));
        Assert.Equal("needs iLok/driver", PluginBrowserListPresenter.ShortRefusal(ProbeRefusal.CopyProtectionText));
    }
}
