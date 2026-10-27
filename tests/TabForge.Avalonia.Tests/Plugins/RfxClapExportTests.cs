using System.Collections.Generic;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the CLAP plug-ins of an exported FX chain file: the CLAP block text (header line, STATE lines, bypass, mix, preset), the instrument form, and the gaps when the engine
/// reports no CLAP description.
/// Does not own: the VST blocks (RfxVstFixTests, RfxReviewFixTests), reading CLAP descriptions (the engine).
/// Tests: this class.
/// </summary>
public class RfxClapExportTests
{
    private static LoadedPluginInfo Info(string id = "org.example.gain", string name = "Gain", string vendor = "Example") =>
        new(LoadedPluginInfo.Clap, 0, id, -1, -1, true, vendor, name);

    private static RfxChainExporter.Item Item(LoadedPluginInfo? info, byte[]? state = null, bool instrument = false, bool enabled = true, int wet = 100, string preset = "") =>
        new("Gain", "Example", "gain.clap", PluginFormats.Clap, instrument, enabled, wet, state, info, PresetName: preset);

    [Fact]
    public void AClapPluginIsWrittenAsAClapBlockWithItsStateAsBase64()
    {
        var result = RfxChainExporter.Export(new[] { Item(Info(), new byte[] { 1, 2, 3 }) });
        Assert.Equal(1, result.Written);
        Assert.Empty(result.Notes);
        var lines = result.Text.Split("\r\n");
        Assert.Equal("BYPASS 0 0", lines[0]);
        Assert.Equal("<CLAP \"CLAP: Gain (Example)\" org.example.gain \"\"", lines[1]);
        Assert.Equal("  <STATE", lines[2]);
        Assert.Equal("    AQID", lines[3]);
        Assert.Equal("  >", lines[4]);
        Assert.Equal(">", lines[5]);
        Assert.Equal("FLOATPOS 0 0 0 0", lines[6]);
        Assert.StartsWith("FXID {", lines[7]);
        Assert.Equal("WAK 0 0", lines[8]);
    }

    [Fact]
    public void AnInstrumentIsClapiAndABypassedMixedPluginKeepsThoseLines()
    {
        var text = RfxChainExporter.Export(new[] { Item(Info(), null, instrument: true, enabled: false, wet: 50, preset: "Warm pad") }).Text;
        Assert.Contains("BYPASS 1 0\r\n", text);
        Assert.Contains("<CLAP \"CLAPi: Gain (Example)\" org.example.gain \"\"\r\n>\r\n", text);   // no state: no STATE block
        Assert.Contains("WET 0.5 0\r\n", text);
        Assert.Contains("PRESETNAME \"Warm pad\"\r\n", text);
    }

    [Fact]
    public void LongStatesBreakIntoLinesOfOwnAndInsideAContainerTheBlockIsIndented()
    {
        var state = new byte[200];
        var inner = Item(Info(), state);
        var box = new RfxChainExporter.Item("Box", "", "", "", false, true, 100, null, null, null, new[] { inner });
        var text = RfxChainExporter.Export(new[] { box }).Text;
        Assert.Contains("  BYPASS 0 0 0\r\n  <CLAP \"CLAP: Gain (Example)\" org.example.gain \"\"\r\n    <STATE\r\n", text);
        var base64Lines = 0;
        foreach (var line in text.Split("\r\n")) if (line.StartsWith("      ") && line.Trim().Length > 0) base64Lines++;
        Assert.Equal(3, base64Lines);   // 200 bytes: 96 + 96 + 8
    }

    [Fact]
    public void ANameWithQuotesCannotBreakTheHeaderLine()
    {
        var text = RfxChainExporter.Export(new[] { Item(Info(name: "Say \"hi\"", vendor: "A\"B")) }).Text;
        Assert.Contains("<CLAP \"CLAP: Say 'hi' (A'B)\" org.example.gain \"\"\r\n", text);
    }

    [Fact]
    public void ANotLoadedOrWrongKindPluginIsNamedInTheNotesAndLeftOut()
    {
        var result = RfxChainExporter.Export(new[] { Item(null), Item(LoadedPluginInfo.Unavailable), Item(new LoadedPluginInfo(LoadedPluginInfo.Vst2, 1, "", 2, 2, true, "", "")), Item(Info(id: "")) });
        Assert.Equal(0, result.Written);
        Assert.Equal("", result.Text);
        Assert.Single(result.Notes);
        Assert.StartsWith("Exported 0 of 4 plug-ins.", result.Notes[0]);
    }

    [Fact]
    public void ASlotWithAClapPathBecomesAClapItem()
    {
        var slot = new PluginSlot { Name = "Gain", Vendor = "Example", Path = "C:/Plug-ins/gain.clap", Format = PluginFormats.Clap, State = System.Convert.ToBase64String(new byte[] { 9 }) };
        var items = RfxChainExporter.FromSlots(new[] { slot }, new LoadedPluginInfo?[] { Info() });
        Assert.Equal(PluginFormats.Clap, items[0].Format);
        Assert.Equal("gain.clap", items[0].FileName);
        Assert.Contains("<CLAP ", RfxChainExporter.Export(items).Text);
    }

    [Fact]
    public void TheClapInfoRoundTripsOnTheWire()
    {
        using var stream = new System.IO.MemoryStream();
        using (var w = new System.IO.BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) Info(id: "org.example." + new string('x', 100)).Write(w);
        stream.Position = 0;
        using var r = new System.IO.BinaryReader(stream);
        Assert.Equal(Info(id: "org.example." + new string('x', 100)), LoadedPluginInfo.Read(r));
    }
}
