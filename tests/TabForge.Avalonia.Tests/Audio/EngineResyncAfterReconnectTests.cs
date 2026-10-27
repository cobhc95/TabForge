using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: that a command the engine never received is not remembered as sent: after the engine could not be reached (pipe down) and comes back, or after its
//   device was reopened (driver change), it gets the song's current clip list and mute / level again.
// Does not own: the engine's own handling of the clip list (EngineSplitClipGapTests) or the split and drag (SplitMovePlaybackSyncTests).
// Tests: AudioEngineClient.Send / SyncAudio / ResyncIfDropped / NoteReady.
public sealed class EngineResyncAfterReconnectTests
{
    private sealed class FixedFileSystem : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    private sealed class ImmediateContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => d(state);
    }

    /// <summary>A client with no engine process whose link can be cut, and a stand-in engine that keeps the last clip list it was given while the link was up.</summary>
    private sealed class Rig : IDisposable
    {
        public readonly SongProject Project = TemplateFactory.Blank();
        public readonly TrackModel Track;
        public readonly AudioEngineClient Client = new(new TabForge.Plugins.PluginTrust());
        public List<ClipSpec>? EngineClips;
        public bool LinkUp = true;
        private readonly string _file = Path.Combine(Path.GetTempPath(), "tf-resync-" + Guid.NewGuid().ToString("N") + ".wav");
        private readonly IMediaFileSystem _saved = MediaPathPolicy.FileSystem;

        public Rig()
        {
            Track = NewTrackFactory.CreateTrack(Project, TrackKind.Audio);
            Project.Tracks.Add(Track);
            File.WriteAllBytes(_file, new byte[44]);
            MediaPathPolicy.FileSystem = new FixedFileSystem();
            MediaAccess.Evaluate(_file, null);
            Track.AudioClips.Add(new AudioClip { File = _file, Name = "take", StartSec = 0, SourceLengthSec = 12, FileLengthSec = 12 });
            Client.AttachFakeForTest();
            Client.SetUiForTest(new ImmediateContext());
            Client.ClipsSentForTest = (slot, clips) => { if (LinkUp && slot == Client.SlotOf(Track)) EngineClips = clips.ToList(); };
        }

        public void Sync() => Client.Sync(Project.Tracks, new PluginSettings(), t => (t.Mute ? 0 : t.Volume, t.Pan), Project);

        public void Link(bool up, bool resyncAtOnce = true) { LinkUp = up; Client.SetFakeLinkForTest(up, resyncAtOnce); }

        /// <summary>The whole clip becomes two pieces three seconds apart.</summary>
        public void SplitAndMoveApart()
        {
            var whole = Track.AudioClips[0];
            whole.SourceLengthSec = 4;
            Track.AudioClips.Add(new AudioClip { File = whole.File, Name = "take", StartSec = 8, OffsetSec = 4, SourceLengthSec = 4, FileLengthSec = 12 });
        }

        public void Dispose()
        {
            Client.Dispose();
            MediaPathPolicy.FileSystem = _saved;
            File.Delete(_file);
        }
    }

    [Fact]
    public void ClipsAndMuteEditedWhileTheEngineWasUnreachableReachItWhenItIsBack()
    {
        using var rig = new Rig();
        rig.Sync();
        Assert.Equal(12, Assert.Single(rig.EngineClips!).SourceLengthSec, 6);
        var slot = rig.Client.SlotOf(rig.Track);

        rig.Link(false);
        rig.SplitAndMoveApart();
        rig.Track.Mute = true;
        rig.Sync();
        Assert.Null(rig.Client.TrackMixSentForTest(slot));   // nothing reached the engine, so nothing is remembered as sent
        Assert.Single(rig.EngineClips!);                      // the engine still holds the old whole clip

        var resynced = 0;
        rig.Client.Resynced += () => resynced++;
        rig.Link(true);
        Assert.Equal(1, resynced);
        Assert.Equal(2, rig.EngineClips!.Count);
        var pieces = rig.EngineClips.OrderBy(c => c.StartSec).ToArray();
        Assert.Equal(0, pieces[0].StartSec, 6);
        Assert.Equal(4, pieces[0].SourceLengthSec, 6);
        Assert.Equal(8, pieces[1].StartSec, 6);
        Assert.Equal(4, pieces[1].OffsetSec, 6);
        Assert.Equal(0, rig.Client.TrackMixSentForTest(slot)!.Value.Volume);   // muted
    }

    [Fact]
    public void TheNextSyncAfterTheLinkIsBackSendsClipsChainsAndDeviceAgain()
    {
        using var rig = new Rig();
        rig.Sync();
        var chainLoads = rig.Client.ChainLoadsSentForTest;
        rig.Link(false);
        rig.SplitAndMoveApart();
        rig.Sync();
        rig.Sync();   // repeated syncs while down keep trying, none is remembered
        rig.Link(true, resyncAtOnce: false);
        var commands = new List<EngineCommand>();
        rig.Client.SentForTest = commands.Add;
        rig.Sync();
        Assert.Contains(EngineCommand.SetClips, commands);
        Assert.Contains(EngineCommand.Configure, commands);
        Assert.True(rig.Client.ChainLoadsSentForTest > chainLoads, "the chains were not sent again");
        Assert.Equal(2, rig.EngineClips!.Count);
    }

    [Fact]
    public void AReopenedDeviceSendsTheAudioStateAgainButKeepsTheLoadedChains()
    {
        using var rig = new Rig();
        rig.Sync();
        rig.Client.RaiseReadyForTest();   // the first Ready of an engine is its normal start
        var chainLoads = rig.Client.ChainLoadsSentForTest;
        var resynced = 0;
        rig.Client.Resynced += () => resynced++;
        rig.EngineClips = null;           // the driver changed: the engine's new mix holds no clips

        rig.Client.RaiseReadyForTest();   // a second Ready: the device was reopened
        Assert.Equal(1, resynced);
        Assert.Single(rig.EngineClips!);
        Assert.Equal(chainLoads, rig.Client.ChainLoadsSentForTest);   // the plug-in chains were not reloaded
    }

    [Fact]
    public void AnEngineThatWasNeverDroppedGetsNoSecondCopy()
    {
        using var rig = new Rig();
        rig.Sync();
        var commands = new List<EngineCommand>();
        rig.Client.SentForTest = commands.Add;
        rig.Sync();
        Assert.DoesNotContain(EngineCommand.SetClips, commands);
    }
}
