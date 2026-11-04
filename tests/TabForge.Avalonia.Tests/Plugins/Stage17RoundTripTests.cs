using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Presets;
using TabForge.Services;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

// Owns: Stage 17 project round trips with bridged plug-ins: a project saved on one OS opens
// on the other and back with its plug-in state preserved, and a missing plug-in stays readable.
// Does not own: the file format (ProjectService), the live bridge (Vst32WineLinuxTests) or CI fixtures.
// Tests: this class. The Windows job writes the reference file; the Linux job opens it.
[Collection("Vst32Native")]
public sealed class Stage17RoundTripTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-stage17-roundtrip-" + Guid.NewGuid().ToString("N"));
    private readonly ITestOutputHelper _output;

    public Stage17RoundTripTests(ITestOutputHelper output)
    {
        _output = output;
        Directory.CreateDirectory(_folder);
    }

    public void Dispose()
    {
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static byte[] ReferenceState() =>
        Enumerable.Range(0, 64).Select(i => (byte)((i * 7 + 3) & 0xFF)).ToArray();

    private static SongProject ProjectWithBridgedSlot(string pluginPath, byte[] state)
    {
        var project = new SongProject { Title = "stage17-ref" };
        project.Tracks.Add(new TrackModel { Name = "Synth", Measures = TemplateFactory.Measures(1) });
        project.Tracks[0].Rig.Plugins.Add(new PluginSlot
        {
            Name = "Stage17Ref",
            Path = pluginPath,
            Format = "VST2",
            Type = PluginSlotType.Instrument,
            Enabled = true,
            State = Convert.ToBase64String(state),
        });
        return project;
    }

    // A foreign-absolute plug-in path (Windows form on Linux, Unix form on Windows) must load:
    // it can never resolve against this process folder; the engine reports it missing readably.
    [Fact]
    public void ForeignAbsolutePluginPathIsAcceptedByValidation()
    {
        var foreign = OperatingSystem.IsWindows() ? "/vst/stage17ref.dll" : @"C:\VST\Stage17Ref.dll";
        var project = ProjectWithBridgedSlot(foreign, ReferenceState());
        var path = Path.Combine(_folder, "foreign.tforge");

        ProjectService.Save(path, project);
        var reopened = ProjectService.Load(path);

        var slot = Assert.Single(reopened.Tracks[0].Rig.Plugins);
        Assert.Equal(foreign, slot.Path);
        Assert.Equal(Convert.ToBase64String(ReferenceState()), slot.State);
    }

    // The bridged slot's own state must survive save, load and re-save byte for byte.
    [Fact]
    public void BridgedSlotStateSurvivesProjectSaveAndReload()
    {
        var local = OperatingSystem.IsWindows() ? @"C:\VST\Stage17Local.dll" : "/vst/stage17local.dll";
        var project = ProjectWithBridgedSlot(local, ReferenceState());
        var first = Path.Combine(_folder, "first.tforge");
        var second = Path.Combine(_folder, "second.tforge");

        var hash1 = ProjectService.Save(first, project);
        var reopened = ProjectService.Load(first);
        Assert.Equal(Convert.ToBase64String(ReferenceState()), Assert.Single(reopened.Tracks[0].Rig.Plugins).State);
        var hash2 = ProjectService.Save(second, reopened);

        Assert.Equal(hash1, hash2);
        _output.WriteLine($"roundtrip state_bytes={ReferenceState().Length} hash_match=True");
    }

    // Windows CI writes this reference file (TABFORGE_STAGE17_REF); the Linux job opens it.
    // Without the variable it still proves the local save/reload leg.
    [Fact]
    public void RoundTrip_WritesReferenceProject()
    {
        var local = OperatingSystem.IsWindows() ? @"C:\VST\Stage17Ref.dll" : "/vst/stage17ref.dll";
        var project = ProjectWithBridgedSlot(local, ReferenceState());
        var reference = Environment.GetEnvironmentVariable("TABFORGE_STAGE17_REF");
        var path = string.IsNullOrWhiteSpace(reference)
            ? Path.Combine(_folder, "reference.tforge")
            : reference;
        if (!string.IsNullOrWhiteSpace(reference))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);

        ProjectService.Save(path, project);
        var reopened = ProjectService.Load(path);

        Assert.Equal(local, Assert.Single(reopened.Tracks[0].Rig.Plugins).Path);
        Assert.Equal(Convert.ToBase64String(ReferenceState()), reopened.Tracks[0].Rig.Plugins.Single().State);
        _output.WriteLine($"reference written path={path}");
    }

    // Linux CI opens the Windows-saved reference (TABFORGE_STAGE17_REF_IN), verifies it, re-saves
    // ("and back") and reloads; the re-saved bytes go to TABFORGE_STAGE17_BACK for the record.
    [Fact]
    public void RoundTrip_OpensReferenceAndSavesItBack()
    {
        var referenceIn = Environment.GetEnvironmentVariable("TABFORGE_STAGE17_REF_IN");
        string path;
        string expectedPath;
        if (string.IsNullOrWhiteSpace(referenceIn) || !File.Exists(referenceIn))
        {
            expectedPath = OperatingSystem.IsWindows() ? @"C:\VST\Stage17Ref.dll" : "/vst/stage17ref.dll";
            path = Path.Combine(_folder, "local-ref.tforge");
            ProjectService.Save(path, ProjectWithBridgedSlot(expectedPath, ReferenceState()));
            _output.WriteLine("no Windows reference supplied; proving the local save/load/save leg");
        }
        else
        {
            path = referenceIn;
            expectedPath = OperatingSystem.IsWindows() ? "/vst/stage17ref.dll" : @"C:\VST\Stage17Ref.dll";
            _output.WriteLine($"opening cross-OS reference path={path}");
        }

        var opened = ProjectService.Load(path);
        var slot = Assert.Single(opened.Tracks[0].Rig.Plugins);
        Assert.Equal(Convert.ToBase64String(ReferenceState()), slot.State);
        if (!string.IsNullOrWhiteSpace(referenceIn) && File.Exists(referenceIn))
            Assert.Equal(expectedPath, slot.Path);

        var back = Environment.GetEnvironmentVariable("TABFORGE_STAGE17_BACK");
        var backPath = string.IsNullOrWhiteSpace(back) ? Path.Combine(_folder, "back.tforge") : back;
        if (!string.IsNullOrWhiteSpace(back))
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(backPath))!);
        var hashBefore = ProjectService.Save(backPath, opened);
        var reloaded = ProjectService.Load(backPath);
        var hashAfter = ProjectService.Save(Path.Combine(_folder, "back2.tforge"), reloaded);

        Assert.Equal(Convert.ToBase64String(ReferenceState()), Assert.Single(reloaded.Tracks[0].Rig.Plugins).State);
        Assert.Equal(hashBefore, hashAfter);
        _output.WriteLine($"roundtrip back path={backPath} stable=True");
    }

    // A project naming a plug-in that is not on disk must still load, and the missing-plugin
    // message must name it readably instead of crashing.
    [Fact]
    public void MissingPluginIsReadableNotACrash()
    {
        var missing = OperatingSystem.IsWindows() ? @"C:\VST\Gone-Missing-17.dll" : "/vst/gone-missing-17.dll";
        var project = ProjectWithBridgedSlot(missing, ReferenceState());
        var path = Path.Combine(_folder, "missing.tforge");

        ProjectService.Save(path, project);
        var reopened = ProjectService.Load(path);

        Assert.Equal(missing, Assert.Single(reopened.Tracks[0].Rig.Plugins).Path);
        var message = VideoExportPresenter.MissingPluginsMessage(new[] { missing });
        Assert.Contains(missing, message);
        Assert.Contains("not loaded", message);
        var specs = new PluginTrust().BuildSpecs(
            reopened.Tracks[0].Rig.Plugins, new List<string>(), new PluginSettings(), new NoTrust());
        Assert.Single(specs);
        _output.WriteLine($"missing readable path={missing}");
    }

    // Live leg under Wine: the deterministic plug-in's state goes through the file and back
    // into the bridge, and the restored parameter proves the bytes were preserved.
    [Vst32WineFact]
    public void Wine_BridgedStateSurvivesFileRoundTrip()
    {
        using var plugin = Vst32TestAssets.StartWine(Vst32TestAssets.TestPluginPath!, instrument: false, 1024).Plugin;
        plugin.SetOfflineMode(true);
        Assert.True(plugin.SetParameter(0, 0.75));
        var state = plugin.GetState();
        Assert.NotNull(state);
        Assert.NotEmpty(state);

        var project = ProjectWithBridgedSlot(Vst32TestAssets.TestPluginPath!, state!);
        var path = Path.Combine(_folder, "wine-state.tforge");
        ProjectService.Save(path, project);
        var reopened = ProjectService.Load(path);
        var restored = Convert.FromBase64String(Assert.Single(reopened.Tracks[0].Rig.Plugins).State!);

        Assert.True(plugin.SetParameter(0, 0.25));
        plugin.SetState(restored);
        Assert.InRange(plugin.GetParameters().Single().Value, 0.70, 0.80);
        _output.WriteLine($"wine file state state_bytes={state!.Length} restored=True");
    }

    private sealed class NoTrust : IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }
}
