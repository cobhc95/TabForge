using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Playback;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the end-to-end guard that audio takes recorded on two tracks at once while the song already plays mid-song (over a repeat)
//   land at the engine's song second (never mapped a second time) and survive a save and reload as .tforge and as .gp with the
//   embedded project, their files resolving in the song's Media folder; the landing shows no "Recorded N s" notice.
// Does not own: the engine capture (Recorder tests), the media copy question (MediaSaveStepTests).
// Tests: this class.
public sealed class RecordedTakeRoundTripTests
{
    [AvaloniaFact]
    public void TwoTracksRecordedWhilePlayingMidSongLandAtTheEngineSecondAndSurviveSaveAndReload()
    {
        using var client = AvaloniaPlaybackComposition.CreateEngineClient(new TabForge.Plugins.PluginTrust());
        client.AttachFakeForTest();
        var song = new SongDocument(() => null) { Project = SongDocument.BlankProject() };
        var score = song.Project.Tracks[0];
        score.Measures[1].RepeatStart = true;
        score.Measures[2].RepeatEnd = true;
        var lead = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        lead.Name = "Lead Harmony";
        var piano = NewTrackFactory.CreateTrack(song.Project, TrackKind.Audio);
        piano.Name = "Piano";
        song.Project.Tracks.Add(lead);
        song.Project.Tracks.Add(piano);
        lead.RecordArm = piano.RecordArm = true;
        song.MarkClean();

        var folder = Path.Combine(Path.GetTempPath(), "tf-avalonia-tests", "RecordedTakeRoundTrip" + Guid.NewGuid().ToString("N"));
        var media = MediaFolders.ForSong(Path.Combine(folder, "Song.tforge"));
        Directory.CreateDirectory(media);
        var leadFile = Path.Combine(media, "Lead Harmony 20261026-100232 s5.wav");
        var pianoFile = Path.Combine(media, "Piano 20261026-100232 s11.wav");
        File.WriteAllBytes(leadFile, new byte[] { 1 });
        File.WriteAllBytes(pianoFile, new byte[] { 1 });

        var playback = new AvaloniaPlaybackSessionTests.FakeEngine();
        using var session = new AvaloniaPlaybackSession(playback, new AvaloniaUiThread(Dispatcher.UIThread), () => song.Project);
        var tabs = new MainWindowTabs(song, _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var statuses = new System.Collections.Generic.List<string>();
        var hooks = new RecordingPresenter.EngineHooks(client, tabs, statuses.Add);
        // The engine stamps the take on its song clock (already performed song seconds). A timeline compiled mid-song maps
        // scheduler seconds far ahead of that; the take must never go through it (the bug landed one take ~30 bars late).
        var startSec = AudioClipTimelineRules.SecondsAtBar(song.Project, 2, 0.5);
        try
        {
            hooks.BeginLive(session, song.Project.Tracks, toSong: sec => sec + 60, startedWhilePlaying: true);
            hooks.BeginLanding(song.Undo.Snapshot(song.Project, "Record takes"));
            hooks.EndLive();
            client.RaiseRecordedForTest(lead, leadFile, startSec, 16.4);
            client.RaiseRecordedForTest(piano, pianoFile, startSec, 16.4);
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            hooks.Dispose();
            tabs.Dispose();
        }
        Assert.DoesNotContain(statuses, text => text.StartsWith("Recorded", StringComparison.Ordinal));
        AssertTake(song.Project, "Lead Harmony", leadFile, startSec, null);
        AssertTake(song.Project, "Piano", pianoFile, startSec, null);
        Assert.Equal(1, song.Undo.UndoCount);   // both takes are one undo step

        try
        {
            var tforge = Path.Combine(folder, "Song.tforge");
            MediaStoredPaths.Write(song.Project, null, tforge, () => ProjectService.Save(tforge, song.Project));
            var loaded = ProjectService.Load(tforge);
            AssertTake(loaded, "Lead Harmony", leadFile, startSec, folder);
            AssertTake(loaded, "Piano", pianoFile, startSec, folder);

            var gp = Path.Combine(folder, "Song.gp");
            MediaStoredPaths.Write(song.Project, null, gp, () => { GuitarProExporter.Save(song.Project, gp); return null; });
            var fromGp = GuitarProImporter.Import(gp);
            AssertTake(fromGp, "Lead Harmony", leadFile, startSec, folder);
            AssertTake(fromGp, "Piano", pianoFile, startSec, folder);
            Assert.True(File.Exists(leadFile) && File.Exists(pianoFile));   // a recorded WAV is never deleted
        }
        finally
        {
            try { Directory.Delete(folder, true); } catch (IOException) { } // swallow: best-effort test cleanup
        }
    }

    private static void AssertTake(SongProject project, string trackName, string file, double startSec, string? baseFolder)
    {
        var clip = Assert.Single(project.Tracks.Single(track => track.Name == trackName).AudioClips);
        Assert.Equal(startSec, clip.StartSec, 6);
        Assert.Equal(16.4, clip.SourceLengthSec, 6);
        Assert.False(clip.Muted);
        Assert.Equal(Path.GetFullPath(file), MediaStoredPaths.Resolve(clip.File, baseFolder), ignoreCase: true);
    }
}
