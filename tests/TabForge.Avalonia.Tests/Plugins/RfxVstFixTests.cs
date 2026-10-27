using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;
using TabForge.Plugins;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the fixes the real plug-in sweep found: the export keeps the preset name and the indentation and BYPASS fields of the host's own files, a 4-channel
/// container with empty pin blocks is no routing, a container mix and a VST3 to VST2 fallback are named on import, a file whose plug-ins are all missing says
/// so, 32-bit DLLs are listed by the scan, probed vendors reach the import list, and the load error carries the Windows error.
/// Does not own: the basic import and export (RfxImportMatchTests, RfxReviewFixTests, RfxExportGapTests).
/// Tests: this class.
/// </summary>
public class RfxVstFixTests
{
    private static LoadedPluginInfo Info(string name = "Plug") => new(LoadedPluginInfo.Vst2, 7, "", 2, 2, true, "Acme", name);

    [Fact]
    public void ThePresetNameSurvivesAnExportAndAnImport()
    {
        var slots = new List<PluginSlot> { new() { Name = "Plug", Path = @"C:\vst\plug.dll", Format = "VST2", PresetName = "Lead Tone" } };
        var text = RfxChainExporter.Export(RfxChainExporter.FromSlots(slots, new[] { Info() })).Text;
        Assert.Contains("PRESETNAME \"Lead Tone\"", text);
        Assert.Equal("Lead Tone", ReaperChainImporter.Parse(text).Entries.Single().Preset);
    }

    [Fact]
    public void ContainersAreWrittenIndentedWithThreeFieldBypassLikeReaper()
    {
        var slots = new List<PluginSlot> { new() { Name = "Plug", Path = @"C:\vst\plug.dll", Format = "VST2" } };
        var containers = new List<ChainContainer> { new() { Name = "Box", Start = 0, Count = 1 } };
        var text = RfxChainExporter.Export(RfxChainExporter.FromTree(ChainTree.Tree(slots, containers), slots, new[] { Info() })).Text;
        Assert.Contains("BYPASS 0 0\r\n<CONTAINER Container \"Box\"\r\n  CONTAINER_CFG 2 2 2 0\r\n", text);
        Assert.Contains("\r\n  BYPASS 0 0 0\r\n  <VST \"VST: Plug (Acme)\" plug.dll", text);
        Assert.Contains("\r\n  >\r\n  FLOATPOS", text);
        Assert.Contains("\r\n>\r\n", text);
        Assert.Single(ReaperChainImporter.Parse(text).Entries);
    }

    [Fact]
    public void AFourChannelContainerWithEmptyPinBlocksIsNoRouting()
    {
        const string empty = "<CONTAINER Container \"\"\n  CONTAINER_CFG 4 2 2 0\n  <IN_PINS\n  >\n  <OUT_PINS\n  >\n  BYPASS 0 0 0\n  <VST \"VST: P (Acme)\" p.dll 0 \"\" 1<00> \"\"\n  >\n>\n";
        var parsed = ReaperChainImporter.Parse(empty);
        Assert.DoesNotContain(parsed.Notes, n => n.Contains("internal routing"));
        var built = ReaperChainImporter.Build(parsed, new[] { new VstPluginInfo("P", @"C:\vst\p.dll", "VST2", "Acme") });
        Assert.False(ChainTree.Valid(built.Chain[0].ChainContainers, 1).Single().OwnRouting);

        var routed = ReaperChainImporter.Parse(empty.Replace("<IN_PINS\n  >", "<IN_PINS\n    1 2\n  >"));
        Assert.Contains(routed.Notes, n => n.Contains("internal routing"));
    }

    [Fact]
    public void AContainerMixIsNamedBecauseItIsNotApplied()
    {
        var parsed = ReaperChainImporter.Parse("<CONTAINER Container \"Wide\"\n  <VST \"VST: P (Acme)\" p.dll 0 \"\" 1<00> \"\"\n  >\n>\nWET 0.5 0\n");
        var built = ReaperChainImporter.Build(parsed, new[] { new VstPluginInfo("P", @"C:\vst\p.dll", "VST2", "Acme") });
        Assert.Contains("Container mix is kept with the chain and written back on export, but not applied to the sound: Wide (50%).", built.Report);
    }

    [Fact]
    public void AVst3EntryFallsBackToTheVst2VersionWithoutItsSavedSettings()
    {
        var entry = new ReaperChainImporter.Entry("Bulldozer", "Acme", "Bulldozer.vst3", "VST3", false, true, new byte[] { 1, 2, 3, 4 }, null);
        var vst2 = new VstPluginInfo("Bulldozer", @"C:\vst\Bulldozer.dll", "VST2", "Acme");
        Assert.Same(vst2, ReaperChainImporter.Match(entry, new[] { vst2 }));
        Assert.Null(ReaperChainImporter.Match(entry, new[] { vst2 with { Vendor = "Someone Else" } }));
        var built = ReaperChainImporter.Build(new ReaperChainImporter.Parsed(new List<ReaperChainImporter.Entry> { entry }, new List<string>()), new[] { vst2 });
        Assert.Contains("Using the VST2 version of Bulldozer.", built.Report);
        Assert.Contains("its saved settings are for the VST3 version", built.Report);
        Assert.Null(built.Chain.Single().State);
        Assert.Equal("VST2", built.Chain.Single().Format);
    }

    [Fact]
    public void WhenEveryPlugInIsMissingTheNoticeNamesTheCountTheFileAndTheFolders()
    {
        var parsed = ReaperChainImporter.Parse("<VST \"VST: A (X)\" a.dll 0 \"\" 1<00> \"\"\n>\n<VST \"VST: B (X)\" b.dll 0 \"\" 2<00> \"\"\n>\n");
        var built = ReaperChainImporter.Build(parsed, Array.Empty<VstPluginInfo>(), new[] { @"C:\vst" });
        Assert.Equal(@"None of the 2 plug-ins in Rhythm.RfxChain were found. Searched: C:\vst.", built.NoneFoundNotice("Rhythm.RfxChain"));
        var read = ChainFileApply.Check("Rhythm.RfxChain", built.Chain, 0, true, built.NoneFoundNotice("Rhythm.RfxChain"));
        Assert.False(read.Outcome.Applied);
        Assert.StartsWith("None of the 2 plug-ins", read.Outcome.Notice);
        Assert.Contains("has no plug-ins", ChainFileApply.Check("x.RfxChain", new List<PluginSlot>(), 0, true).Outcome.Notice);
    }

    [Fact]
    public void AScanListsA32BitVst2DllAsNotSupported()
    {
        var dir = Directory.CreateTempSubdirectory("pe32");
        try
        {
            var old32 = Path.Combine(dir.FullName, "Old32.dll");
            var new64 = Path.Combine(dir.FullName, "New64.dll");
            File.WriteAllBytes(old32, PeImage(x86: true));
            File.WriteAllBytes(new64, PeImage(x86: false));
            Assert.True(PeExports.IsVst2Plugin32(old32));
            Assert.False(PeExports.IsVst2Plugin(old32));
            Assert.True(PeExports.IsVst2Plugin(new64));
            Assert.False(PeExports.IsVst2Plugin32(new64));
            var found = VstScannerService.Scan(new[] { dir.FullName }, CancellationToken.None, new PluginTrust(), new NoFiles(), forceRefresh: true);
            Assert.Equal(VstPluginInfo.Format32, found.Single(p => p.Name == "Old32").Format);
            Assert.True(found.Single(p => p.Name == "Old32").IsUnsupported32);
            Assert.Equal("VST2", found.Single(p => p.Name == "New64").Format);
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void AProbedVendorFillsAScanEntryWithoutOne()
    {
        var settings = new PluginSettings();
        settings.LastScan = new List<VstPluginInfo> { new("Bulldozer", @"C:\vst\Bulldozer.dll", "VST2") };
        settings.Probed.Add(new KnownPlugin { Path = @"C:\VST\bulldozer.dll", Role = "Effect", Vendor = "Audio Assault", Format = "VST2" });
        Assert.All(KnownPluginList.Of(settings, Array.Empty<PluginSlot>()), p => Assert.Equal("Audio Assault", p.Vendor));
    }

    [Theory]
    [InlineData(193, "32-bit plug-in; TabForge loads 64-bit plug-ins only")]
    [InlineData(126, "a file it needs is missing")]
    [InlineData(5, "access denied")]
    [InlineData(1114, "library could not be loaded")]
    public void ALoadFailureNamesTheReasonAndTheWindowsError(int code, string text)
    {
        var message = Vst2LoadError.Describe(code);
        Assert.Contains(text, message);
        Assert.Contains($"Windows error {code}", message);
    }

    private sealed class NoFiles : TabForge.Platform.IPluginFileTrust
    {
        public string? FinalPath(string fullPath) => null;
        public bool HasValidEmbeddedSignature(string fullPath) => false;
    }

    /// <summary>The smallest PE image that exports VSTPluginMain: one section holding the export table.</summary>
    private static byte[] PeImage(bool x86)
    {
        var image = new byte[0x400];
        void U16(int at, int v) => BitConverter.TryWriteBytes(image.AsSpan(at, 2), (ushort)v);
        void U32(int at, int v) => BitConverter.TryWriteBytes(image.AsSpan(at, 4), (uint)v);
        System.Text.Encoding.ASCII.GetBytes("MZ").CopyTo(image, 0);
        U32(0x3C, 0x40);
        System.Text.Encoding.ASCII.GetBytes("PE").CopyTo(image, 0x40);
        var optionalSize = x86 ? 224 : 240;
        U16(0x44, x86 ? 0x14C : 0x8664); U16(0x46, 1); U16(0x54, optionalSize);
        const int optional = 0x58;
        U16(optional, x86 ? 0x10B : 0x20B);
        var directories = optional + (x86 ? 96 : 112);
        U32(directories, 0x1000); U32(directories + 4, 0x100);
        var section = optional + optionalSize;
        U32(section + 8, 0x200); U32(section + 12, 0x1000); U32(section + 16, 0x200); U32(section + 20, 0x200);
        const int table = 0x200;   // raw pointer of the section: RVA 0x1000
        U32(table + 24, 1); U32(table + 28, 0x1028); U32(table + 32, 0x1030);
        U32(table + 0x30, 0x1040);
        System.Text.Encoding.ASCII.GetBytes("VSTPluginMain").CopyTo(image, table + 0x40);
        return image;
    }
}
