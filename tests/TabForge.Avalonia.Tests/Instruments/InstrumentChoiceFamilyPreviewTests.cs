using System.Linq;
using TabForge.Models;
using TabForge.Ports;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the family-switch preview sound with a fake sink (no engine): a switch to the other
/// family never live-switches the drum channel (no Begin), a stopped song auditions on the new
/// channel, and a playing switch auditions too (the note is the only preview).
/// Does not own: the real engine path (InstrumentChoiceTests) or the picker windows.
/// </summary>
public sealed class InstrumentChoiceFamilyPreviewTests
{
    private sealed class RecordingSink : IProgramSink
    {
        public bool IsPlaying { get; set; }
        public int Begins { get; private set; }
        public int Ends { get; private set; }
        public bool LastKeep { get; private set; }
        public int Auditions { get; private set; }
        public int LastAuditionChannel { get; private set; } = -1;
        public void BeginPreview(int deviceId, int channel, int program) => Begins++;
        public void EndPreview(int deviceId, int channel, bool keep) { Ends++; LastKeep = keep; }
        public void Audition(int deviceId, int channel, int program, int note, int lengthMs)
        {
            Auditions++;
            LastAuditionChannel = channel;
        }
    }

    [Fact]
    public void PlayingFamilySwitchAuditionsOnceWithoutALiveSwitch()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var sink = new RecordingSink { IsPlaying = true };
        var choice = new InstrumentChoice(song, track, sink);
        choice.Preview(kit.Name);
        Assert.Equal(0, sink.Begins);
        Assert.Equal(1, sink.Auditions);
        Assert.Equal(9, sink.LastAuditionChannel);
        choice.Cancel();
        // The Cancel fix restores every touched channel: the original plus the
        // audition channel the family switch moved to (shared output leak guard).
        Assert.Equal(2, sink.Ends);
        Assert.False(sink.LastKeep);
    }

    [Fact]
    public void StoppedFamilySwitchAuditionsOnTheNewChannel()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var sink = new RecordingSink { IsPlaying = false };
        var choice = new InstrumentChoice(song, track, sink);
        choice.Preview(kit.Name);
        Assert.Equal(0, sink.Begins);
        Assert.Equal(1, sink.Auditions);
        Assert.Equal(9, sink.LastAuditionChannel);
        choice.Cancel();
    }

    [Fact]
    public void PlayingSameFamilySwitchesLiveWithNoAudition()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var target = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program != track.MidiProgram);
        var sink = new RecordingSink { IsPlaying = true };
        var choice = new InstrumentChoice(song, track, sink);
        choice.Preview(target.Name);
        Assert.Equal(1, sink.Begins);
        Assert.Equal(0, sink.Auditions);
        choice.Cancel();
    }
}
