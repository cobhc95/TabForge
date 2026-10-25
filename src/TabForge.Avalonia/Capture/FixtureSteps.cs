using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using TabForge.Avalonia.Features.Recording.Presenter;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Capture;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Capture;

// Owns: the verbs that make up song content a capture needs and the real song does not have: audio-fixture (audio tracks with synthetic clips over the repeat or at the song's end),
//   record-fake (a growing live take on an armed track, fed by a made-up input level, then the clip it lands as) and timeline-scroll.
// Does not own: the made-up media (CaptureFixtures), the pointer drags (PointerSteps), the run loop (CaptureSession).
// Tests: TabForge.Avalonia.Tests.CaptureFixtureTests.
internal sealed class FixtureSteps(CaptureSession session)
{
    private const double MsPerSecond = 1000.0;
    private readonly string _media = Path.Combine(UserPaths.Local, "capture-media");   // inside the scratch profile, which the run deletes
    private LiveTakes? _live;
    private int _takeRow = -1;   // the armed track's row: looked up in the document each time, never held
    private double _takeStartSec;

    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs)
    {
        verbs["audio-fixture"] = step => Run(() => AddFixture(step.Text));
        verbs["record-fake"] = step => Run(() => RecordFake(step));
        verbs["timeline-scroll"] = step => Run(() => Scroll(step));
    }

    private async Task Run(Action change)
    {
        change();
        await session.SettleAsync();
    }

    private TrackModel TakeTrack => session.Document.Project.Tracks[_takeRow];

    private TimelineView Timeline => session.Find<ArrangementView>().Timeline;

    /// <summary>"repeat": a long clip from before the repeat to after it, and a pickup clip that starts inside its second pass (the first pass shows hatched); "end": a short clip near the song's end, for a drag past it.</summary>
    private void AddFixture(string kind)
    {
        var heldPass = -1;
        DocumentEdits.Run(session.Document, p =>
        {
            var timing = SongExtent.Timing(p);
            if (kind == "end")
            {
                var barSec = timing.BarSec;
                AddTrack(p, "Take", timing.EndSec - 6 * barSec, 4 * barSec);
                return true;
            }
            if (kind != "repeat") throw new InvalidOperationException($"Unknown audio fixture \"{kind}\".");
            var first = timing.Performed.First(b => b.Bar == 16).StartMs / MsPerSecond;
            var last = timing.Performed.Last(b => b.Bar == 24).EndMs / MsPerSecond;
            var secondPass = timing.Performed.Where(b => b.Bar == 19).Skip(1).First().StartMs / MsPerSecond;
            AddTrack(p, "Vocal take", first, last - first);
            AddTrack(p, "Pickup", secondPass, last - secondPass);
            heldPass = timing.Performed.Select((b, i) => (b, i)).Where(x => x.b.Bar == 19).Skip(1).First().i;
            return true;
        });
        if (heldPass >= 0) session.HoldMoment(heldPass, 0.5);   // the playhead inside the second pass: the ruler shows its "bar - pass" label
    }

    private void AddTrack(SongProject project, string name, double startSec, double lengthSec)
    {
        var track = NewTrackFactory.CreateTrack(project, TrackKind.Audio);
        track.Name = name;
        track.AudioClips.Add(Clip(name, startSec, lengthSec));
        project.Tracks.Add(track);
    }

    private AudioClip Clip(string name, double startSec, double lengthSec)
    {
        var file = CaptureFixtures.WriteWave(_media, name.Replace(' ', '-'), lengthSec);
        ApproveFolder(file, session.Document.Media);
        return new AudioClip { File = file, Name = name, StartSec = startSec, SourceLengthSec = lengthSec, FileLengthSec = lengthSec };
    }

    /// <summary>The import's own step for a picked file: its folder is approved for the song, so the app reads the file and computes the peaks itself.</summary>
    internal static void ApproveFolder(string file, MediaContext media)
    {
        var verdict = MediaPathPolicy.Classify(file, media.BaseDirectory);
        if (MediaAccess.Decide(verdict, media).State == MediaAccessState.NeedsApproval) MediaAccess.Approve(verdict, media);
    }

    /// <summary>"start" (bar): arms a new audio track and begins a take there; "grow" (sec): the take has run that long, the made-up input level drawn into it; "stop": the take lands as a clip.</summary>
    private void RecordFake(CaptureStep step)
    {
        var timeline = Timeline;
        if (step.Text is "grow" or "stop" && _live is null) throw new InvalidOperationException($"record-fake \"{step.Text}\" needs a \"start\" first.");
        switch (step.Text)
        {
            case "start":
                DocumentEdits.Run(session.Document, p =>
                {
                    var take = NewTrackFactory.CreateTrack(p, TrackKind.Audio);
                    take.Name = "Audio take";
                    take.RecordArm = true;
                    p.Tracks.Add(take);
                    _takeRow = p.Tracks.Count - 1;
                    return true;
                });
                _takeStartSec = AudioClipTimelineRules.SecondsAtBar(session.Document.Project, step.Option("bar", 1) - 1, 0);
                _live = new LiveTakes { LaneCreated = _ => timeline.Reload(session.Document.Project) };
                timeline.UseLiveTakes(_live);
                break;
            case "grow":
                var seconds = step.Option("sec", 1.0);
                for (var at = 0.0; at <= seconds; at += 0.1) _live!.Frame(TakeTrack, _takeStartSec + at, (float)(0.2 + 0.6 * Math.Abs(Math.Sin(at * 7))), midi: false);
                timeline.RefreshLiveTakes();
                break;
            case "stop":
                _live!.Remove(TakeTrack);
                DocumentEdits.Run(session.Document, p => { p.Tracks[_takeRow].RecordArm = false; p.Tracks[_takeRow].AudioClips.Add(Clip("Audio take", _takeStartSec, step.Option("sec", 3.0))); return true; });
                timeline.RefreshLiveTakes();
                break;
            default:
                throw new InvalidOperationException($"Unknown record-fake action \"{step.Text}\".");
        }
    }

    /// <summary>Scrolls the timeline sideways to "end" or to bar N (1-based) at the left edge.</summary>
    private void Scroll(CaptureStep step)
    {
        var timeline = Timeline;
        if (step.Option("target") == "score")
        {
            // A horizontally scrolling score: bar N of the song at the left edge, by its share of the scrolled width.
            var score = session.Find<ScorePreviewView>().GetVisualAncestors().OfType<ScrollViewer>().First();
            score.UpdateLayout();
            score.Offset = new Vector(Math.Clamp((step.Int - 1.0) / Math.Max(1, timeline.BarCount) * score.Extent.Width, 0, Math.Max(0, score.Extent.Width - score.Viewport.Width)), score.Offset.Y);
            return;
        }
        var scroller = timeline.GetVisualAncestors().OfType<ScrollViewer>().First();
        scroller.UpdateLayout();
        var x = step.Value.ValueKind == System.Text.Json.JsonValueKind.String ? Math.Max(0, scroller.Extent.Width - scroller.Viewport.Width) : timeline.BarX(step.Int - 1);
        scroller.Offset = new Vector(x, scroller.Offset.Y);
    }
}
