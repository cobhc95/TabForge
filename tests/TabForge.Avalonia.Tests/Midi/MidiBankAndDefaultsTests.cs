using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using TabForge.Core.Midi;
using TabForge.Core.Midi.Processors;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the per-track bank select checks (CC0/CC32 before the program change in the compiled setup,
/// nothing emitted when off, save/load round trip with old files defaulting to off).
/// Does not own: the compiler, the track model or the project file format.
/// Tests: this class.
/// </summary>
public class MidiBankSelectTests
{
    private static SongProject SongWithBank(int msb, int lsb)
    {
        var song = DemoSongFactory.Create();
        song.Tracks[0].MidiBankMsb = msb;
        song.Tracks[0].MidiBankLsb = lsb;
        return song;
    }

    private static ScoreTimeline Compile(SongProject song) =>
        MidiTimelineBuilder.Build(song, new PlaybackOptions { StartBar = 0 });

    [Fact]
    public void BankSelectIsEmittedBeforeTheProgramChange()
    {
        var setup = Compile(SongWithBank(1, 2)).ChannelSetup
            .Where(e => e.TrackIndex == 0 && (e.Status & 0xF0) is 0xB0 or 0xC0)
            .Where(e => (e.Status & 0xF0) == 0xC0 || e.Data1 is 0 or 32)
            .Take(3)
            .ToList();

        Assert.Equal(3, setup.Count);
        Assert.Equal(0xB0, setup[0].Status & 0xF0);
        Assert.Equal(0, setup[0].Data1);
        Assert.Equal(1, setup[0].Data2);
        Assert.Equal(0xB0, setup[1].Status & 0xF0);
        Assert.Equal(32, setup[1].Data1);
        Assert.Equal(2, setup[1].Data2);
        Assert.Equal(0xC0, setup[2].Status & 0xF0);
    }

    [Fact]
    public void BankOffEmitsNoBankMessagesButKeepsTheProgram()
    {
        var setup = Compile(SongWithBank(-1, -1)).ChannelSetup
            .Where(e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xB0 && e.Data1 is 0 or 32)
            .ToList();

        Assert.Empty(setup);
        Assert.Contains(Compile(SongWithBank(-1, -1)).ChannelSetup,
            e => e.TrackIndex == 0 && (e.Status & 0xF0) == 0xC0);
    }

    [Fact]
    public void BankTravelsWithTheTimelineEventsTheExportWrites()
    {
        var events = Compile(SongWithBank(4, 7)).Events
            .Where(e => e.TrackIndex == 0 && !e.IsArticulation)
            .ToList();
        var bankMsb = events.FindIndex(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 0 && e.Data2 == 4);
        var bankLsb = events.FindIndex(e => (e.Status & 0xF0) == 0xB0 && e.Data1 == 32 && e.Data2 == 7);
        var program = events.FindIndex(e => (e.Status & 0xF0) == 0xC0);

        Assert.True(bankMsb >= 0 && bankLsb >= 0 && program >= 0);
        Assert.True(bankMsb < bankLsb && bankLsb < program);
    }

    [Fact]
    public void BankSurvivesASaveLoadRoundTrip()
    {
        var song = SongWithBank(3, 9);
        var read = JsonSerializer.Deserialize<SongProject>(JsonSerializer.Serialize(song))!;

        Assert.Equal((3, 9), (read.Tracks[0].MidiBankMsb, read.Tracks[0].MidiBankLsb));
        ProjectValidator.Validate(read);
    }

    [Fact]
    public void FilesWrittenBeforeBankSelectReadAsOff()
    {
        var song = SongWithBank(3, 9);
        var node = JsonNode.Parse(JsonSerializer.Serialize(song))!;
        foreach (var track in node["Tracks"]!.AsArray())
        {
            track!.AsObject().Remove("MidiBankMsb");
            track!.AsObject().Remove("MidiBankLsb");
        }
        var read = node.Deserialize<SongProject>()!;

        Assert.Equal((-1, -1), (read.Tracks[0].MidiBankMsb, read.Tracks[0].MidiBankLsb));
        ProjectValidator.Validate(read);
    }
}

/// <summary>
/// Owns: the processor-default checks (humanizer base delay 0 means no added delay; CC mapper default maps onto a different controller).
/// Does not own: the catalog entries or the engine processors.
/// Tests: this class.
/// </summary>
public class MidiProcessorDefaultsTests
{
    private static MidiParams ParamsOf(string type)
    {
        var proc = MidiProcessorCatalog.Create(MidiProcessorCatalog.Find(type)!);
        return MidiParams.Parse(JsonDocument.Parse(proc.Params));
    }

    private static MidiBuffer Run(IMidiProcessor processor, int status, int data1, int data2)
    {
        var input = new MidiBuffer();
        input.Add(0, (byte)status, (byte)data1, (byte)data2);
        var output = new MidiBuffer();
        processor.Process(input, output, new MidiContext { Frames = 512, SampleRate = 44100, Tempo = 120, Playing = true, BlockStart = 0 });
        return output;
    }

    [Fact]
    public void HumanizerDefaultAddsNoDelay()
    {
        var info = MidiProcessorCatalog.Find("humanize")!;
        Assert.Equal(0, info.Params.First(p => p.Key == "BaseDelayMs").Default);

        // Timing and velocity randomness off, base delay missing (old file): the note leaves on the same frame.
        var output = Run(new HumanizeProcessor(MidiParams.Parse(JsonDocument.Parse("""{"TimingMs":0,"BiasMs":0,"VelRandom":0}""")), 44100), 0x90, 60, 100);

        Assert.Equal(1, output.Count);
        Assert.Equal(0, output.Items[0].Frame);
        Assert.Equal((0x90, 60, 100), (output.Items[0].Status, output.Items[0].Data1, output.Items[0].Data2));
    }

    [Fact]
    public void CcMapperDefaultDoesNotMapOntoItself()
    {
        var info = MidiProcessorCatalog.Find("ccMapper")!;
        var source = info.Params.First(p => p.Key == "Source").Default;
        var target = info.Params.First(p => p.Key == "Target").Default;
        Assert.NotEqual(source, target);

        var output = Run(new CcMapperProcessor(ParamsOf("ccMapper")), 0xB0, (int)source, 64);

        Assert.Equal(1, output.Count);
        Assert.Equal((0xB0, (byte)target, 64), (output.Items[0].Status, output.Items[0].Data1, output.Items[0].Data2));
    }
}
