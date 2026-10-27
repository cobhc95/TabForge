using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Avalonia.Headless.XUnit;
using TabForge.Audio;
using TabForge.Audio.Contracts;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presets;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the proof that imported and recorded audio reach the engine: an import plan lands its clips on the engine's track slot,
// a recorded take lands on its track's chain slot, a section move re-syncs the moved clips, and a live order refresh never
// restarts the playing song — all down the normal clip-edit path.
// Does not own: the audible output (the fake engine takes the commands) or the live order swap (the Core live-edit tests prove it).
// Tests: this file.
[Collection(TimingCollection.Name)]   // the live-refresh check pumps a wall-clock engine: run alone so scheduler contention cannot starve it
public class EngineAudibilityTests
{
    private static SongDocument SongWithClip(out AudioClip clip, out string file)
    {
        var song = new SongDocument(() => null) { Project = DemoSongFactory.Create() };
        var track = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        track.SoundSource = SoundSources.Plugins;
        track.Rig.Plugins.Add(new TabForge.Plugins.PluginSlot { Name = "Synth", Path = @"C:\NoSuch\Synth.vst3", Format = "VST3", Type = TabForge.Plugins.PluginSlotType.Instrument });
        song.Project.Tracks.Add(track);
        file = Path.Combine(Path.GetTempPath(), "tf-audible-" + Guid.NewGuid().ToString("N") + ".wav");
        File.WriteAllBytes(file, new byte[44]);
        clip = new AudioClip { File = file, Name = "take", StartSec = 1, SourceLengthSec = 2, FileLengthSec = 2 };
        track.AudioClips.Add(clip);
        return song;
    }

    private static AudioEngineClient FakeClient() { var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust())!; client.AttachFakeForTest(); return client; }

    /// <summary>A file system that reports every path as an ordinary fixed-drive file, and the approval the engine needs first.</summary>
    private sealed class FixedFileSystem : IMediaFileSystem
    {
        public DriveType DriveTypeOf(string root) => DriveType.Fixed;
        public string? FinalPathOf(string fullPath) => fullPath;
    }

    [AvaloniaFact]
    public void ImportedClipReachesTheEngineDownTheClipEditPath()
    {
        var song = SongWithClip(out var clip, out var file);
        var track = song.Project.Tracks[^1];
        var savedFileSystem = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        MediaAccess.Evaluate(file, null);
        using var client = FakeClient();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), null) { Engine = client };
        var sent = new List<ClipSpec>();
        client.ClipsSentForTest = (_, clips) => sent.AddRange(clips);
        try
        {
            var view = new ArrangementView(song.Project, song);
            view.Timeline.ClipEdited = document => MainWindowClipRefresh.AfterEdit(view, document, null, sessions);

            view.Timeline.ClipEdited.Invoke(song);

            Assert.Contains(sent, spec => Math.Abs(spec.StartSec - clip.StartSec) < 0.001);
        }
        finally
        {
            MediaPathPolicy.FileSystem = savedFileSystem;
            client.Dispose();
            File.Delete(file);
        }
    }

    [Fact]
    public void RecordedTakeLandsOnItsTracksChainSlot()
    {
        var song = SongWithClip(out _, out var file);
        var track = song.Project.Tracks[^1];
        var savedFileSystem = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        MediaAccess.Evaluate(file, null);
        using var client = FakeClient();
        var sent = new List<(int Slot, IReadOnlyList<ClipSpec> Clips)>();
        client.ClipsSentForTest = (slot, clips) => sent.Add((slot, clips));
        try
        {
            // The take the engine reported, landed the way the record stop lands it, then synced down the normal path.
            var takes = RecordedTakes.FromRecording(file, 4, 2, loop: null);
            DocumentEdits.Run(song, _ => { RecordedTakes.Place(track, takes, midi: false); return true; });
            AvaloniaEngineRouting.Apply(song.Project, null, client, new PluginSettings(), 100, song);

            var slot = client.SlotOf(track);
            Assert.True(slot >= 0);
            Assert.Contains(client.RequestedChains, chain => chain.Slot == slot);   // the take's slot runs the track's chain
            var takeClips = sent.Where(s => s.Slot == slot).SelectMany(s => s.Clips).ToList();
            Assert.Contains(takeClips, spec => Math.Abs(spec.StartSec - 4) < 0.001);
        }
        finally
        {
            MediaPathPolicy.FileSystem = savedFileSystem;
            client.Dispose();
            File.Delete(file);
        }
    }

    [Fact]
    public void SectionMoveResyncsTheMovedClips()
    {
        var song = SongWithClip(out var clip, out var file);
        var savedFileSystem = MediaPathPolicy.FileSystem;
        MediaPathPolicy.FileSystem = new FixedFileSystem();
        MediaAccess.Evaluate(file, null);
        using var client = FakeClient();
        using var sessions = new AvaloniaPlaybackSessions(new AvaloniaPlaybackSessionTests.FakeUiThread(), () => new AvaloniaPlaybackSessionTests.FakeEngine()) { Engine = client };
        var session = sessions.For(song)!;
        session.PlayOrPause();
        // The section move shifted the clip: the re-sync must send the new position.
        DocumentEdits.Run(song, _ => { clip.StartSec = 7; return true; });
        var sent = new List<ClipSpec>();
        client.ClipsSentForTest = (_, clips) => sent.AddRange(clips);
        try
        {
            MainWindowClipRefresh.AfterSectionMove(song, new[] { 0 }, null, sessions);

            Assert.Contains(sent, spec => Math.Abs(spec.StartSec - 7) < 0.001);
        }
        finally
        {
            MediaPathPolicy.FileSystem = savedFileSystem;
            client.Dispose();
            File.Delete(file);
        }
    }

    [Fact]
    public void LiveRefreshKeepsThePlayingEngineRunning()
    {
        var project = DemoSongFactory.Create();
        using var engine = new TabForge.Playback.PlaybackEngine(new DropSink());
        using var session = new AvaloniaPlaybackSession(engine, new AvaloniaPlaybackSessionTests.FakeUiThread(), () => project);
        session.PlayOrPause();
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (!engine.IsPlaying && DateTime.UtcNow < deadline)
        {
            session.PumpFrame();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Thread.Sleep(10);
        }
        Assert.True(engine.IsPlaying, "the engine never started playing");

        var bars = project.Tracks.Max(t => t.Measures.Count);
        session.RefreshArrangementLive(project, Enumerable.Range(0, Math.Max(1, bars)).ToArray());

        Assert.True(engine.IsPlaying, "the live order refresh restarted the playing song");
        session.Stop();
    }

    /// <summary>A MIDI sink that drops every message; the check is the engine's playing state, not the sound.</summary>
    private sealed class DropSink : TabForge.Playback.IMidiOutput
    {
        public IReadOnlyList<TabForge.Playback.MidiOutputDeviceInfo> Devices { get; } = Array.Empty<TabForge.Playback.MidiOutputDeviceInfo>();
        public void Send(int deviceId, int status, int data1, int data2) { }
        public void ResetAll() { }
        public void Close() { }
        public void Dispose() { }
    }
}
