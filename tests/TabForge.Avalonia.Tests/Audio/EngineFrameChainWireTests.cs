using System.Collections.Generic;
using System.IO;
using System.Linq;
using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Isolation;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine wire bounds for a plug-in chain (LoadChain, ChainState, ChainCommit) and the other bounded fields a real producer
/// fills, round-tripped at the largest values the app can produce, so no bounded field drops a real plug-in or file.
/// Does not own: the frame writer (EngineFrameWriteTests) or the per-command read limits (FrameLimits).
/// Tests: this class.
/// </summary>
public class EngineFrameChainWireTests
{
    /// <summary>Every format label a chain can carry: the plug-in kinds, the built-in effects, both bridge labels and the legacy 32-bit label.</summary>
    private static readonly string[] Formats =
    {
        PluginFormats.Vst2, PluginFormats.Vst3, PluginFormats.Clap, PluginFormats.Builtin, PluginFormats.Bridged32, PluginFormats.Bridged64,
        PluginFormats.LegacyUnsupported32,
    };

    /// <summary>Path lengths: a typical install path, a deep one, and the app's own cap.</summary>
    private static readonly int[] PathLengths = { 260, 1000, Frames.MaxPathChars };

    private static string Chars(char c, int n) => new(c, n);

    private static string PathOf(int length) => @"C:\" + Chars('p', length - 7) + ".dll";

    /// <summary>A spec with every bounded field at its realistic maximum: name 256, pins 32, id 64, expected SHA-256 64 hex digits.</summary>
    private static PluginSpec MaxSpec(string format, int index) => new(
        Path: PathOf(PathLengths[index % PathLengths.Length]), Format: format, IsInstrument: index % 2 == 0, Enabled: true, Wet: 100, State: null,
        Name: Chars('N', 256), Pins: Chars('P', 32), OutputDb: 12, Id: Chars('i', 64), Skip: false, Untrusted: true,
        ExpectedSha256: Chars('a', 64), LocationTrusted: false);

    /// <summary>Reads every frame of <paramref name="stream"/> and assembles the chain load the way the engine reader does.</summary>
    private static ChainLoadProtocol.Ready? Assemble(MemoryStream stream)
    {
        var assembler = new ChainLoadProtocol.Assembler();
        ChainLoadProtocol.Ready? ready = null;
        stream.Position = 0;
        while (Frames.Read(stream, FrameLimits.ForCommand) is { } frame)
        {
            using (frame)
            {
                if (frame.Type == (byte)EngineCommand.LoadChain) assembler.OnLoadChain(frame.Reader);
                else if (frame.Type == (byte)EngineCommand.ChainState) assembler.OnChainState(frame.Reader);
                else if (frame.Type == (byte)EngineCommand.ChainCommit) ready = assembler.OnCommit(frame.Reader);
            }
        }
        return ready;
    }

    [Fact]
    public void EveryFormatLabelReadsBackWithEveryBoundedFieldAtItsMaximum()
    {
        var specs = Formats.Select((format, i) => MaxSpec(format, i)).ToList();
        var stream = new MemoryStream();
        Frames.Write(stream, (byte)EngineCommand.LoadChain, w => w.Write(specs));
        stream.Position = 0;

        using var frame = Frames.Read(stream, FrameLimits.ForCommand) ?? throw new InvalidDataException("missing frame");
        var read = frame.Reader.ReadChain();

        Assert.Equal(specs, read);
    }

    [Fact]
    public void ChainLoadWithStatesArrivesWholeThroughTheProtocol()
    {
        var specs = Formats.Select((format, i) => MaxSpec(format, i)).ToList();
        var bigState = Chars('A', PluginStateLimits.MaxBase64Chars);   // the largest state the contract allows, one frame
        const string smallState = "c21hbGw=";
        var stream = new MemoryStream();

        ChainLoadProtocol.Send((command, payload) => Frames.Write(stream, (byte)command, payload), 2, false, Chars('t', 256),
            specs.Select((s, i) => i == 1 ? s with { State = bigState } : i == 3 ? s with { State = smallState } : s).ToList(), 7);

        var ready = Assemble(stream) ?? throw new InvalidDataException("chain load was never committed");
        var expected = specs.Select((s, i) => i == 1 ? s with { State = bigState } : i == 3 ? s with { State = smallState } : s).ToList();
        Assert.Equal(2, ready.Slot);
        Assert.Equal(Chars('t', 256), ready.Track);
        Assert.False(ready.UseSynth);
        Assert.Empty(ready.Missing);
        Assert.Equal(expected, ready.Specs);
    }

    [Fact]
    public void LoadedPluginReportKeepsItsMaximumVendorNameAndClassId()
    {
        var info = new LoadedPluginInfo(LoadedPluginInfo.Vst3, 0x41424344u, Chars('c', 256), -1, -1, true, Chars('v', 256), Chars('n', 256));
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) info.Write(w);
        stream.Position = 0;

        using var r = new BinaryReader(stream);
        Assert.Equal(info, LoadedPluginInfo.Read(r));
    }

    [Fact]
    public void AFailureMessageCarriesTheLongestPath()
    {
        // A helper failure can name a 32767-char plug-in path; the reply must arrive whole, not be dropped as too long.
        var path = PathOf(Frames.MaxPathChars);
        var stream = new MemoryStream();
        Frames.Write(stream, RemotePlugin.MsgFailed, w => { w.Write(7u); w.WriteString(path); });
        stream.Position = 0;

        using var frame = Frames.Read(stream, IsolatedFrameLimits.ForReply) ?? throw new InvalidDataException("missing failure frame");
        Assert.Equal(RemotePlugin.MsgFailed, frame.Type);
        Assert.Equal(7u, frame.Reader.ReadUInt32());
        Assert.Equal(path, PluginControlChannel.FailureText(frame.Reader.ReadBytes((int)(frame.Reader.BaseStream.Length - frame.Reader.BaseStream.Position))));
    }

    [Fact]
    public void AuditedBoundsKeepTheirRealMaximums()
    {
        // Clip file and device fields: a 32767-char path, a 256-char device name, a 64-char driver name.
        var clips = new List<ClipSpec> { new(PathOf(Frames.MaxPathChars), 1, 0, 60, -96, 0, 1) };
        var config = new EngineConfig(Chars('d', 64), Chars('v', 256), 48000, 256, true, Chars('i', 256));
        var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            w.Write(clips);
            w.Write(config);
        }
        stream.Position = 0;

        using var r = new BinaryReader(stream);
        Assert.Equal(clips, r.ReadClips());
        Assert.Equal(config, r.ReadConfig());
    }
}
