using System;
using System.IO;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the engine event reader: a fault on its thread (a handler that throws, a closed pipe) is logged and counts as the engine ending,
/// and never leaves the reader thread as an unhandled exception.
/// Tests: this class.
/// </summary>
public sealed class EngineReaderCrashTests
{
    // The frames a peer sent, then its close: a plain stream, so no pipe, thread or timing is involved.
    private static MemoryStream Sent(Action<MemoryStream> write)
    {
        var stream = new MemoryStream();
        write(stream);
        stream.Position = 0;
        return stream;
    }

    // Runs posted work at once on the calling thread, as the reader thread does when no UI context is set.
    private sealed class InlineContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    [Fact]
    public void AHandlerThatThrowsDropsThatFrameAndKeepsReading()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        engine.AttachForTest(SharedBlock.Create($"tf-reader-test-{Guid.NewGuid():N}"), new InlineContext(), 0);
        var faults = 0;
        engine.OnHandlerFault = _ => faults++;
        engine.PluginSlow += (_, _, _) => throw new InvalidOperationException("handler fault");
        var crashed = 0;
        engine.PluginCrashed += _ => crashed++;
        using (var server = Sent(s =>
        {
            Frames.Write(s, (byte)EngineEvent.PluginSlow, w => { w.Write(1); w.Write(1); w.WriteString("a.dll"); w.Write(0); w.Write(5); });
            Frames.Write(s, (byte)EngineEvent.PluginCrashed, w => { w.Write(1); w.Write(1); w.WriteString("b.dll"); });
        }))
        {
            var exception = Record.Exception(() => engine.ReadEvents(server));

            Assert.Null(exception);
            Assert.Equal(1, faults);      // the bad frame was reported, not hidden
            Assert.Equal(1, crashed);     // the frame after it was still read
        }
        engine.DisposeRetiredForTest();
    }

    [Fact]
    public void ARejectedCommandAndAnUnknownEventAreLoggedAndReadingContinues()
    {
        Assert.Equal(26, (int)EngineEvent.CommandRejected);   // golden: the wire value
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        engine.AttachForTest(SharedBlock.Create($"tf-reader-test-{Guid.NewGuid():N}"), new InlineContext(), 0);
        var faults = 0;
        engine.OnHandlerFault = _ => faults++;
        var crashed = 0;
        engine.PluginCrashed += _ => crashed++;
        using (var server = Sent(s =>
        {
            Frames.Write(s, (byte)EngineEvent.CommandRejected, w => { w.Write((byte)7); w.WriteString("unknown command"); });
            Frames.Write(s, 250, w => { });   // an event this build does not know
            Frames.Write(s, (byte)EngineEvent.PluginCrashed, w => { w.Write(1); w.Write(1); w.WriteString("b.dll"); });
        }))
        {
            Assert.Null(Record.Exception(() => engine.ReadEvents(server)));
            Assert.Equal(0, faults);
            Assert.Equal(1, crashed);
        }
        engine.DisposeRetiredForTest();
    }

    [Fact]
    public void AClosedPipeEndsTheReadingQuietly()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        engine.AttachFakeForTest();
        using var server = Sent(_ => { });
        Assert.Null(Record.Exception(() => engine.ReadEvents(server)));
    }

    [Fact]
    public void ATakeInFlightOnADeletedTrackIsDiscarded()
    {
        using var engine = new AudioEngineClient(new TabForge.Plugins.PluginTrust());
        engine.AttachFakeForTest();
        var project = new TabForge.Models.SongProject();
        project.Tracks.Clear();
        var track = TabForge.Services.NewTrackFactory.CreateTrack(project, TabForge.Models.TrackKind.Guitar);
        track.SoundSource = TabForge.Models.SoundSources.Plugins;
        track.RecordArm = true;
        project.Tracks.Add(track);
        var plugins = new TabForge.Services.PluginSettings();
        engine.Sync(project.Tracks, plugins, project: project, owner: project);
        var slot = engine.SlotOf(track);
        Assert.True(engine.StartRecording(project.Tracks, System.IO.Path.GetTempPath()));
        var landed = 0;
        engine.Recorded += (_, _, _, _) => landed++;

        project.Tracks.Clear();   // the track is deleted while its take is being recorded
        engine.Sync(project.Tracks, plugins, project: project, owner: project);
        using var server = Sent(s => Frames.Write(s, (byte)EngineEvent.Recorded, w => { w.Write(slot); w.WriteString("take.wav"); w.Write(0.0); w.Write(1.0); }));
        Assert.Null(Record.Exception(() => engine.ReadEvents(server)));

        Assert.Equal(0, landed);
    }
}
