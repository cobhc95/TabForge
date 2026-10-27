using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Transport;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the instrument choice against the real playback engine and a recording MIDI output: a previewed instrument reaches the track's channel at
///     once (stopped or playing) with an audition note when stopped, Cancel sends the original program back and restores the model, Commit is one
///     undo step and the running schedule keeps the new program without stop and play, the current sound is found by program when the name has a suffix,
///     and the note about a plug-in route.
/// Does not own: the picker windows (InstrumentPickerRunTests), the engine's scheduling (PlaybackEngine tests).
/// Tests: InstrumentChoice, EngineProgramSink, InstrumentRouteNote, PlaybackEngine.BeginProgramPreview, MainWindowLiveMix.
/// </summary>
[Collection(TimingCollection.Name)]   // runs a wall-clock playback engine: keep it out of the parallel tests so scheduler contention cannot starve its waits
public class InstrumentChoiceTests
{
    private const int SettleMs = 600, WaitMs = 3000;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewSendsTheProgramToTheTracksChannel_AndAuditionsOnlyWhenStopped(bool playing)
    {
        using var rig = new Rig(playing);
        rig.Choice.Preview(rig.Target.Name);

        Assert.True(Until(() => rig.Output.LastProgram(rig.Track.MidiChannel) == rig.Target.Program), $"channel {rig.Track.MidiChannel} should get program {rig.Target.Program}");
        Thread.Sleep(SettleMs);
        Assert.Equal(!playing, rig.Output.NoteOns(rig.Track.MidiChannel) > 0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CancelSendsTheOriginalProgramBackAndTheModelIsIdentical(bool playing)
    {
        using var rig = new Rig(playing);
        var before = DocumentEdits.Fingerprint(rig.Doc);
        var undo = rig.Doc.Undo.UndoCount;
        rig.Choice.Preview(rig.Target.Name);
        Assert.True(Until(() => rig.Output.LastProgram(rig.Track.MidiChannel) == rig.Target.Program));

        rig.Choice.Cancel();

        Assert.True(Until(() => rig.Output.LastProgram(rig.Track.MidiChannel) == rig.OriginalProgram), "the original program must be sent again");
        Assert.Equal(before, DocumentEdits.Fingerprint(rig.Doc));
        Assert.Equal(undo, rig.Doc.Undo.UndoCount);
        Assert.False(rig.Song.IsDirty);
    }

    [Fact]
    public void CommitIsOneUndoStepAndThePlayingScheduleKeepsTheNewProgramWithoutRestart()
    {
        using var rig = new Rig(playing: true);
        var undo = rig.Doc.Undo.UndoCount;
        rig.Choice.Preview(rig.Target.Name);

        var outcome = rig.Choice.Commit(rig.Doc, rig.Target.Name);

        Assert.True(outcome.Changed);
        Assert.Equal(undo + 1, rig.Doc.Undo.UndoCount);
        Assert.Equal(InstrumentPreviewSession.CommitLabel, rig.Doc.Undo.Latest?.Label);
        Assert.Equal(rig.Target.Name, rig.Track.InstrumentName);
        Assert.True(rig.Sessions.For(rig.Doc)!.IsPlaying, "the song keeps playing");
        Thread.Sleep(SettleMs);
        Assert.Equal(rig.Target.Program, rig.Output.LastProgram(rig.Track.MidiChannel));
    }

    [Fact]
    public void CommittingWithoutADocumentKeepsThePreviewedSoundAndStoresNoStep()
    {
        using var rig = new Rig(playing: false);
        var undo = rig.Doc.Undo.UndoCount;
        rig.Choice.Preview(rig.Target.Name);

        var outcome = rig.Choice.Commit(null, rig.Target.Name);

        Assert.True(outcome.Changed);
        Assert.Equal(undo, rig.Doc.Undo.UndoCount);
        Assert.Equal(rig.Target.Name, rig.Track.InstrumentName);
    }

    [Fact]
    public void TheCurrentSoundIsFoundByProgramWhenTheNameCarriesASuffix()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var entry = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program == 24);
        track.MidiProgram = entry.Program;
        track.InstrumentName = entry.Name + " (6 strings)";

        var current = new InstrumentChoice(song, track, null).Current;

        Assert.Equal(entry.Program, current?.Program);
        Assert.False(current?.IsDrumKit);
    }

    [Fact]
    public void AVstInstrumentRouteShowsTheNoteAndAPlainTrackShowsNone()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio);
        Assert.Null(InstrumentRouteNote.For(track));

        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new PluginSlot { Name = "Sylenth1", Type = PluginSlotType.Instrument });

        Assert.Equal("This track plays through Sylenth1; the GM instrument is used only when the plug-in is off.", InstrumentRouteNote.For(track));
    }

    [Fact]
    public void CommittingWithoutADocumentAuditionsOnceNotTwice()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
        var target = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program != track.MidiProgram);
        var sink = new CountingSink();
        var choice = new InstrumentChoice(song, track, sink);
        choice.Preview(target.Name);

        var outcome = choice.Commit(null, target.Name);

        Assert.True(outcome.Changed);
        Assert.Equal(1, sink.Auditions);
        Assert.Equal(target.Name, track.InstrumentName);
    }

    [Fact]
    public void ADrumKitOnAPitchedTrackWithNotesAsksBeforeTheConversionAndAPitchedOneDoesNot()
    {
        var song = FullDemoSongFactory.Create();
        var track = song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9 && TrackSetup.HasNotes(t));
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit);
        var pitched = InstrumentCatalog.All.First(e => !e.IsDrumKit);
        var choice = new InstrumentChoice(song, track, null);

        Assert.True(choice.RemapsNotes(kit.Name));
        Assert.False(choice.RemapsNotes(pitched.Name));
        Assert.True(TrackSetup.ConversionRemapsNotes(track, toDrums: true));
    }

    [Fact]
    public void CommittingDropsThePreviewOverrideAndUndoSendsTheOldProgramThroughTheLiveMixWatch()
    {
        using var rig = new Rig(playing: true);
        using var watch = new MainWindowLiveMix(rig.Sessions);
        watch.Bind(rig.Doc);
        rig.Choice.Preview(rig.Target.Name);
        Assert.Equal(rig.Target.Program, rig.Sessions.For(rig.Doc)!.Engine is PlaybackEngine live ? live.ProgramPreviewOf(rig.Track.MidiChannel) : -2);

        rig.Choice.Commit(rig.Doc, rig.Target.Name);
        var engine = (PlaybackEngine)rig.Sessions.For(rig.Doc)!.Engine;
        Assert.Equal(-1, engine.ProgramPreviewOf(rig.Track.MidiChannel));

        rig.Output.Clear();
        DocumentEdits.Undo(rig.Doc);
        Assert.True(Until(() => rig.Output.LastProgram(rig.Track.MidiChannel) == rig.OriginalProgram), "the undone instrument must sound at once");
    }

    [Fact]
    public void CancelRestoresTheChannelAnAuditionMovedWhenAKitWasTriedOnAPitchedTrack()
    {
        using var rig = new Rig(playing: false);
        var drumProgram = rig.Song.Tracks.First(t => !t.IsAudio && t.MidiChannel == 9).MidiProgram;
        var kit = InstrumentCatalog.All.First(e => e.IsDrumKit && e.Program != drumProgram);
        rig.Choice.Preview(kit.Name);
        Assert.True(Until(() => rig.Output.LastProgram(9) == kit.Program), "the audition sounds the kit on the drum channel");

        rig.Choice.Cancel();

        Assert.True(Until(() => rig.Output.LastProgram(9) == drumProgram), "the drum channel gets the song's own program again");
        Assert.True(Until(() => rig.Output.LastProgram(rig.Track.MidiChannel) == rig.OriginalProgram));
    }

    private sealed class CountingSink : TabForge.Ports.IProgramSink
    {
        public int Auditions { get; private set; }
        public bool IsPlaying => false;
        public void BeginPreview(int deviceId, int channel, int program) { }
        public void EndPreview(int deviceId, int channel, bool keep) { }
        public void Audition(int deviceId, int channel, int program, int note, int lengthMs) => Auditions++;
    }

    private static bool Until(Func<bool> condition)
    {
        for (var waited = 0; waited < WaitMs; waited += 20)
        {
            if (condition()) return true;
            Thread.Sleep(20);
        }
        return condition();
    }

    /// <summary>A song in a document over sessions whose engines write to one recording output, with a choice on a pitched track.</summary>
    [Fact]
    public void UndoOfAMasterPanPushesThroughTheLiveMixWatch()
    {
        using var rig = new Rig(playing: true);
        using var watch = new MainWindowLiveMix(rig.Sessions);
        watch.Bind(rig.Doc);
        DocumentEdits.Checkpoint(rig.Doc, "Master pan");
        rig.Doc.Project.Mixer.MasterPan = 20;
        DocumentEdits.MarkChanged(rig.Doc, invalidatesTimeline: false);
        watch.Check();
        rig.Output.Clear();
        DocumentEdits.Undo(rig.Doc);
        Assert.Equal(0, rig.Doc.Project.Mixer.MasterPan);
        Assert.True(Until(() => rig.Output.Count(0xB0 | rig.Track.MidiChannel, 10) > 0), "the undone master pan must reach the output at once");
    }

    [Fact]
    public void CommittingWhilePlayingLeavesTheProgramToTheRearmTick()
    {
        using var rig = new Rig(playing: true);
        rig.Choice.Preview(rig.Target.Name);
        Thread.Sleep(SettleMs);
        rig.Output.Clear();
        rig.Choice.Commit(rig.Doc, rig.Target.Name);
        var channel = 0xC0 | rig.Track.MidiChannel;
        Assert.Equal(0, rig.Output.Count(channel, rig.Target.Program));   // no send of its own: the re-arm sends the committed program
        Assert.True(Until(() => rig.Output.Count(channel, rig.Target.Program) > 0), "the re-arm must send the committed program");
    }

    private sealed class Rig : IDisposable
    {
        public Rig(bool playing)
        {
            Song = FullDemoSongFactory.Create();
            Doc = new SongDocument(() => null) { Project = Song };
            Track = Song.Tracks.First(t => !t.IsAudio && t.MidiChannel != 9);
            OriginalProgram = Track.MidiProgram;
            Target = InstrumentCatalog.All.First(e => !e.IsDrumKit && e.Program != Track.MidiProgram);
            Song.IsDirty = false;
            Sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new PlaybackEngine(Output));
            if (playing)
            {
                Sessions.For(Doc)!.PlayOrPause();
                Thread.Sleep(SettleMs);
            }
            Output.Clear();
            Choice = new InstrumentChoice(Song, Track, new EngineProgramSink(Sessions, Doc, () => new PluginSettings()));
        }

        public SongProject Song { get; }
        public SongDocument Doc { get; }
        public TrackModel Track { get; }
        public int OriginalProgram { get; }
        public InstrumentEntry Target { get; }
        public Recorder Output { get; } = new();
        public AvaloniaPlaybackSessions Sessions { get; }
        public InstrumentChoice Choice { get; }

        public void Dispose() => Sessions.Dispose();
    }

    private sealed class Recorder : IMidiOutput
    {
        private readonly List<(int Status, int Data1)> _sent = new();
        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_sent) _sent.Add((status, data1));
        }
        public int? LastProgram(int channel)
        {
            lock (_sent)
            {
                var last = _sent.FindLast(m => m.Status == (0xC0 | channel));
                return last.Status == 0 ? null : last.Data1;
            }
        }
        public int Count(int status, int data1)
        {
            lock (_sent) return _sent.Count(m => m.Status == status && m.Data1 == data1);
        }
        public int NoteOns(int channel)
        {
            lock (_sent) return _sent.Count(m => m.Status == (0x90 | channel));
        }
        public void Clear() { lock (_sent) _sent.Clear(); }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
