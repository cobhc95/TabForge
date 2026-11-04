using System;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Isolation;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: local Windows checks of the helper's VST3 path (named pipe) with the same real fixtures CI downloads,
// plus the Stage 5 Windows route: a 32-bit-only VST3 scans, hand-picks and routes as 32-bit (bridged) to tfvst32.exe.
// The CI proof is Vst3WineLinuxTests; these keep the Windows helper honest without changing the native 64-bit VST3 route.
// Stage 7: the helper hosts the VST3 editor too, so the bridged 32-bit VST3 opens its own window with a real owner.
[Collection("Vst32Native")]
public sealed class Vst3NativeTests
{
    private readonly ITestOutputHelper _output;
    public Vst3NativeTests(ITestOutputHelper output) => _output = output;

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetDesktopWindow();

    [Fact]
    public void The64BitHelperHostsARealVst3Instrument()
    {
        if (!OperatingSystem.IsWindows() || Vst3TestAssets.RealInstrument is null) return;
        using var plugin = Vst3TestAssets.StartNative64(Vst3TestAssets.RealInstrument!, instrument: true).Plugin;
        plugin.SetOfflineMode(true);
        Assert.True(plugin.IsInstrument);
        Assert.Equal(LoadedPluginInfo.Vst3, plugin.Describe().Format);
        Assert.NotEmpty(plugin.GetParameters());
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        var input = new[] { new float[1024], new float[1024] };
        var output = new[] { new float[1024], new float[1024] };
        double sum = 0; var finite = true;
        for (var block = 0; block < 12; block++)
        {
            var midi = block == 0 ? new[] { new BlockMidi { Frame = 0, Status = 0x90, Data1 = 60, Data2 = 100 } } : Array.Empty<BlockMidi>();
            plugin.Process(input, output, 1024, midi, Vst32TestAssets.Transport(block, 1024));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block > 1) foreach (var sample in output[0]) sum += sample * (double)sample;
        }
        Assert.True(finite);
        Assert.True(Math.Sqrt(sum / (10 * 1024)) > 1e-4, "the native 64-bit VST3 must make sound");
        _output.WriteLine($"native vst3 64 params={plugin.GetParameters().Count} state={state!.Length} latency={plugin.LatencySamples}");
    }

    [Fact]
    public void The32BitHelperHostsARealVst3Effect()
    {
        if (!OperatingSystem.IsWindows() || Vst3TestAssets.RealEffect32 is null) return;
        using var plugin = Vst3TestAssets.StartNative32(Vst3TestAssets.RealEffect32!, instrument: false).Plugin;
        plugin.SetOfflineMode(true);
        Assert.False(plugin.IsInstrument);
        Assert.Equal(LoadedPluginInfo.Vst3, plugin.Describe().Format);
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);
        var changed = parameters.FirstOrDefault(p => !p.ReadOnly);
        if (changed is not null) plugin.SetParameter(changed.Id, changed.Value > 0.5 ? 0.1 : 0.9);
        _output.WriteLine($"native vst3 32 params={parameters.Count} state={state!.Length} latency={plugin.LatencySamples}");
    }

    [WindowsFact]
    public void A32BitOnlyVst3ScansAndRoutesAsBridgedOnWindows()
    {
        // Synthetic layout first: no real file needed, nothing is loaded.
        using var folder = new Vst32TestAssets.TempFolder();
        var flat32 = Path.Combine(folder.Path, "Synth32.vst3");
        var flat64 = Path.Combine(folder.Path, "Synth64.vst3");
        File.WriteAllBytes(flat32, Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));
        File.WriteAllBytes(flat64, Vst32TestAssets.PeImage(x86: false, export: "GetPluginFactory"));

        Assert.True(Vst3Windows.Is32BitOnly(flat32));
        Assert.False(Vst3Windows.Is32BitOnly(flat64));
        Assert.True(PluginInfoProbe.UsesVst32Helper(flat32));
        Assert.False(PluginInfoProbe.UsesVst32Helper(flat64));
        Assert.Equal(PluginFormats.Bridged32, PluginFormats.OfPlugin(flat32));
        Assert.Equal(PluginFormats.Vst3, PluginFormats.OfPlugin(flat64));

        var trust = new PluginTrust();
        var found = VstScannerService.Scan(new[] { folder.Path }, CancellationToken.None, trust, new Vst32TestAssets.NoFiles(), forceRefresh: true);
        Assert.Equal(PluginFormats.Bridged32, found.Single(r => r.Path == flat32).Format);
        Assert.Equal(PluginFormats.Vst3, found.Single(r => r.Path == flat64).Format);

        var hand32 = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(flat32, out var problem32);
        Assert.Null(problem32);
        Assert.Equal(PluginFormats.Bridged32, hand32!.Format);
        var hand64 = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(flat64, out var problem64);
        Assert.Null(problem64);
        Assert.Equal(PluginFormats.Vst3, hand64!.Format);

        var spec32 = new PluginSpec(flat32, PluginFormats.Bridged32, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "s32", Id: "v", LocationTrusted: false, ExpectedSha256: "");
        var spec64 = new PluginSpec(flat64, PluginFormats.Vst3, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "s64", Id: "v", LocationTrusted: false, ExpectedSha256: "");
        var stale32 = new PluginSpec(flat32, PluginFormats.Vst3, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "s32", Id: "v", LocationTrusted: false, ExpectedSha256: "");
        Assert.True(Vst32Bridge.IsBridged(spec32));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(spec32, separateProcess: false));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(spec32, separateProcess: true));
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(stale32, separateProcess: false));
        Assert.False(Vst32Bridge.IsBridged(spec64));
        Assert.Equal(Vst32Bridge.PluginRoute.InProcess, Vst32Bridge.Route(spec64, separateProcess: false));

        // The real 32-bit fixture (TDR Nova 2.2.3, maker release, hash-pinned in wine-stage4): same labels from its PE.
        if (Vst3TestAssets.RealEffect32 is { } real32)
        {
            Assert.True(Vst3Windows.Is32BitOnly(real32), "TDR Nova Win32 must read as 32-bit-only without loading");
            Assert.True(PluginInfoProbe.UsesVst32Helper(real32));
            Assert.Equal(PluginFormats.Bridged32, PluginFormats.OfPlugin(real32));
            var handReal = TabForge.Avalonia.Views.Fx.PluginBrowserSession.FromFile(real32, out var problemReal);
            Assert.Null(problemReal);
            Assert.Equal(PluginFormats.Bridged32, handReal!.Format);
            var realSpec = new PluginSpec(real32, PluginFormats.Bridged32, IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "nova32", Id: "v", LocationTrusted: false, ExpectedSha256: "");
            var realStale = new PluginSpec(real32, "VST3", IsInstrument: false, Enabled: true, Wet: 100, State: null, Name: "nova32", Id: "v", LocationTrusted: false, ExpectedSha256: "");
            Assert.True(Vst32Bridge.IsBridged(realSpec));
            Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(realSpec, separateProcess: false));
            Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(realStale, separateProcess: false));
            _output.WriteLine($"windows vst3 32 route path={real32} format={handReal.Format}");
        }

        // 64-bit stays native: the Dexed bundle and (when present) TDR Nova x64.
        if (Vst3TestAssets.RealInstrument is { } real64)
        {
            Assert.False(Vst3Windows.Is32BitOnly(real64));
            Assert.Equal(PluginFormats.Vst3, PluginFormats.OfPlugin(real64));
            var spec = new PluginSpec(real64, PluginFormats.Vst3, IsInstrument: true, Enabled: true, Wet: 100, State: null, Name: "dexed", Id: "v", LocationTrusted: false, ExpectedSha256: "");
            Assert.False(Vst32Bridge.IsBridged(spec));
            Assert.Equal(Vst32Bridge.PluginRoute.InProcess, Vst32Bridge.Route(spec, separateProcess: false));
        }
        if (Vst3TestAssets.RealEffect64 is { } realEff64)
        {
            Assert.False(Vst3Windows.Is32BitOnly(realEff64));
            Assert.Equal(PluginFormats.Vst3, PluginFormats.OfPlugin(realEff64));
        }
    }

    [WindowsFact]
    public void A32BitVst3ApprovalAndTrustKeepsTheBridgedLabel()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var flat32 = Path.Combine(folder.Path, "Approve32.vst3");
        File.WriteAllBytes(flat32, Vst32TestAssets.PeImage(x86: true, export: "GetPluginFactory"));

        var settings = new PluginSettings();
        var trust = new PluginTrust();
        Assert.False(trust.IsTrusted(flat32, settings, new Vst32TestAssets.NoFiles()));
        trust.Approve(settings, flat32, new Vst32TestAssets.NoFiles());
        Assert.True(trust.IsTrusted(flat32, settings, new Vst32TestAssets.NoFiles()));
        Assert.Equal(64, trust.ProbeHash(flat32, settings, new Vst32TestAssets.NoFiles()).Length);

        // Approve-all identifies through the catalog; the remembered format stays bridged.
        PluginCatalog.ProbeOverride = (_, hash, _) => hash.Length == 64 ? ("Effect", "Acme", "Approve32") : null;
        try
        {
            var described = PluginCatalog.Describe(flat32, settings, trust, new Vst32TestAssets.NoFiles());
            Assert.Equal(("Effect", "Acme"), (described.Role, described.Vendor));
            Assert.Equal(PluginFormats.Bridged32, Assert.Single(settings.Probed).Format);
        }
        finally { PluginCatalog.ProbeOverride = null; }

        // An approved path with no stored format still lists as bridged.
        var approved = new PluginSettings();
        approved.ApprovedPluginPaths.Add(flat32);
        var row = Assert.Single(KnownPluginList.Of(approved, Array.Empty<PluginSlot>()));
        Assert.Equal(PluginFormats.Bridged32, row.Format);
    }

    [Fact]
    public void A32BitVst3EffectProcessesAudioThroughTheWindowsBridgeRoute()
    {
        if (!OperatingSystem.IsWindows() || Vst3TestAssets.RealEffect32 is null) return;
        if (Vst32TestAssets.HelperPath is null) return;
        var path = Vst3TestAssets.RealEffect32!;
        var folder = Path.GetDirectoryName(Vst32TestAssets.HelperPath)!;
        var spec = Vst3TestAssets.Spec(path, instrument: false);
        Assert.Equal(Vst32Bridge.PluginRoute.Bridged32, Vst32Bridge.Route(spec, separateProcess: false));

        // The launch is the named-pipe helper with the VST3 format, like 32-bit VST2.
        var launch = Vst32Bridge.Launch(spec, helperFolder: folder);
        Assert.Equal(Vst32Bridge.HelperPath(folder), launch.ExecutablePath);
        Assert.Equal(PluginHostLaunch.Vst32HostMode, launch.Mode);
        Assert.Equal("VST3", launch.Format);
        var args = launch.Arguments("session", 4321, 48000, 256, spec, null);
        Assert.Equal("VST3", args[6]);

        using var plugin = Vst3TestAssets.StartRouted(path, instrument: false).Plugin;
        plugin.SetOfflineMode(true);
        Assert.False(plugin.IsInstrument);
        Assert.Equal(LoadedPluginInfo.Vst3, plugin.Describe().Format);
        Assert.True(plugin.HasEditor, "the 32-bit helper must report the VST3 editor");
        // The helper needs a real owner HWND on Windows; the desktop window is a valid top-level owner for this check.
        var editor = plugin.OpenEditor(GetDesktopWindow(), docked: false);
        Assert.NotNull(editor);
        Assert.True(editor!.Value.Width > 0 && editor.Value.Height > 0, "the VST3 editor must report its size");
        plugin.CloseEditor();
        var parameters = plugin.GetParameters();
        Assert.NotEmpty(parameters);
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);

        const int frames = 1024;
        var input = new[] { new float[frames], new float[frames] };
        var output = new[] { new float[frames], new float[frames] };
        double inSum = 0, outSum = 0;
        var count = 0;
        var finite = true;
        var phase = 0.0;
        for (var block = 0; block < 8; block++)
        {
            for (var i = 0; i < frames; i++)
            {
                var sample = (float)(0.25 * Math.Sin(phase));
                phase += 2 * Math.PI * 1000 / 48000;
                input[0][i] = sample;
                input[1][i] = sample;
            }
            plugin.Process(input, output, frames, ReadOnlySpan<BlockMidi>.Empty, Vst32TestAssets.Transport(block, frames));
            finite &= Vst32TestAssets.AllFinite(output[0], output[1]);
            if (block < 2) continue;
            for (var i = 0; i < frames; i++)
            {
                inSum += input[0][i] * (double)input[0][i];
                outSum += output[0][i] * (double)output[0][i];
                count++;
            }
        }
        Assert.True(finite, "bridged 32-bit VST3 output must be finite");
        var rmsOut = count == 0 ? 0 : (float)Math.Sqrt(outSum / count);
        Assert.True(rmsOut > 1e-6f, $"bridged 32-bit VST3 must not output silence (RMS {rmsOut:0.00000})");
        _output.WriteLine($"windows vst3 32 bridged params={parameters.Count} state={state!.Length} latency={plugin.LatencySamples} rms={rmsOut:0.00000} editor={plugin.HasEditor}");
    }
}
