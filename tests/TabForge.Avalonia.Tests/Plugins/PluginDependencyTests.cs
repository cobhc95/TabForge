using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the Stage 13 A3 regression proof that an approval covers the DLLs Windows would load from the plug-in's own folder,
//   and that a planted or changed local dependency is refused before LoadLibrary. Does not own: the load itself.
// Tests: this class.
public sealed class PluginDependencyTests
{
    [Native64Fact]
    public void APEImportClosureFindsAPlantedSystemNamedDllInThePluginFolder()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var plugin = folder.Write("passthrough64.dll", File.ReadAllBytes(Vst32TestAssets.TestPluginPath64!));
        Assert.Empty(PeImports.LocalDependencies(plugin));   // a clean folder: its imports are system DLLs
        var clean = PeImports.DependencyHash(plugin);

        var planted = folder.Write("KERNEL32.dll", new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03 });
        Assert.Contains(planted, PeImports.LocalDependencies(plugin), StringComparer.OrdinalIgnoreCase);
        Assert.NotEqual(clean, PeImports.DependencyHash(plugin));
    }

    [Native64Fact]
    public void AnApprovalRefusesADependencyPlantedAfterIt()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var plugin = folder.Write("passthrough64.dll", File.ReadAllBytes(Vst32TestAssets.TestPluginPath64!));
        var settings = new PluginSettings();
        var trust = new PluginTrust();
        var files = new Vst32TestAssets.NoFiles();
        trust.Approve(settings, plugin, files);
        var spec = Spec(trust, settings, files, plugin);
        Assert.False(string.IsNullOrEmpty(spec.ExpectedDependencies));
        using (PluginIdentity.Hold(spec)) { }   // the clean closure loads

        folder.Write("KERNEL32.dll", new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03 });
        var error = Assert.Throws<PluginChangedException>(() => PluginIdentity.Hold(spec));
        Assert.Contains("dependencies", error.Message, StringComparison.OrdinalIgnoreCase);

        File.Delete(Path.Combine(folder.Path, "KERNEL32.dll"));
        using (PluginIdentity.Hold(spec)) { }   // removing the plant returns to the approved closure
    }

    [Native64Fact]
    public void AChangedLocalDependencyIsRefused()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var plugin = folder.Write("passthrough64.dll", File.ReadAllBytes(Vst32TestAssets.TestPluginPath64!));
        var dependency = folder.Write("KERNEL32.dll", new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03 });
        var settings = new PluginSettings();
        var trust = new PluginTrust();
        var files = new Vst32TestAssets.NoFiles();
        trust.Approve(settings, plugin, files);
        var spec = Spec(trust, settings, files, plugin);
        using (PluginIdentity.Hold(spec)) { }

        File.WriteAllBytes(dependency, new byte[] { 0x4D, 0x5A, 0x09, 0x08, 0x07 });
        var error = Assert.Throws<PluginChangedException>(() => PluginIdentity.Hold(spec));
        Assert.Contains("dependencies", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Native64Fact]
    public void ALegacyApprovalWithoutADependencyRecordIsRefusedWhenTheClosureIsNotEmpty()
    {
        using var folder = new Vst32TestAssets.TempFolder();
        var plugin = folder.Write("passthrough64.dll", File.ReadAllBytes(Vst32TestAssets.TestPluginPath64!));
        folder.Write("KERNEL32.dll", new byte[] { 0x4D, 0x5A, 0x01, 0x02, 0x03 });
        var settings = new PluginSettings();
        var trust = new PluginTrust();
        var files = new Vst32TestAssets.NoFiles();
        trust.Approve(settings, plugin, files);
        settings.TrustRecords.Single(r => r.Path.Equals(plugin, StringComparison.OrdinalIgnoreCase)).Dependencies = "";
        var spec = Spec(trust, settings, files, plugin);
        Assert.Equal("", spec.ExpectedDependencies);
        var error = Assert.Throws<PluginChangedException>(() => PluginIdentity.Hold(spec));
        Assert.Contains("does not cover them", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static PluginSpec Spec(PluginTrust trust, PluginSettings settings, Vst32TestAssets.NoFiles files, string plugin)
    {
        var slot = new PluginSlot { Name = Path.GetFileNameWithoutExtension(plugin), Path = plugin, Format = PluginFormats.Vst2 };
        return trust.BuildSpecs(new[] { slot }, new List<string>(), settings, files).Single();
    }
}
