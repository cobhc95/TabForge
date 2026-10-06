using System.IO;
using TabForge.Controllers;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Services;

namespace TabForge;

/// <summary>Long audio clip edits preserve a live or paused MIDI transport while every track grows.</summary>
public static partial class SelfTest
{
    private const int LongAudioMarkerPitch = 77;

    private sealed class LongAudioOutput : IMidiOutput
    {
        private readonly object _gate = new();
        private readonly List<(int Status, int Pitch, int Velocity)> _messages = new();
        private int _resetCount;
        private int _blockedOff;
        private int _offWaitReleased;

        public IReadOnlyList<MidiOutputDeviceInfo> Devices { get; } = Array.Empty<MidiOutputDeviceInfo>();
        public ManualResetEventSlim FirstSend { get; } = new();
        public ManualResetEventSlim MarkerOn { get; } = new();
        public ManualResetEventSlim MarkerOffBlocked { get; } = new();
        public ManualResetEventSlim ReleaseMarkerOff { get; } = new();
        public int ResetCount => Volatile.Read(ref _resetCount);
        public bool MarkerOffWaitReleased => Volatile.Read(ref _offWaitReleased) != 0;
        public (int Status, int Pitch, int Velocity)[] Messages { get { lock (_gate) return _messages.ToArray(); } }

        public bool WaitForResetCount(int count, TimeSpan timeout) =>
            SpinWait.SpinUntil(() => ResetCount >= count, timeout);

        public void Send(int deviceId, int status, int data1, int data2)
        {
            lock (_gate) _messages.Add((status & 0xF0, data1, data2));
            FirstSend.Set();
            if ((status & 0xF0) == 0x90 && data2 > 0 && data1 == LongAudioMarkerPitch) MarkerOn.Set();
            if (((status & 0xF0) == 0x80 || ((status & 0xF0) == 0x90 && data2 == 0))
                && data1 == LongAudioMarkerPitch && Interlocked.Exchange(ref _blockedOff, 1) == 0)
            {
                MarkerOffBlocked.Set();
                Volatile.Write(ref _offWaitReleased, ReleaseMarkerOff.Wait(TimeSpan.FromSeconds(3)) ? 1 : 0);
            }
        }

        public void ResetAll() => Interlocked.Increment(ref _resetCount);
        public void Close() { }
        public void Dispose() => ReleaseMarkerOff.Set();
    }

    private sealed class LongAudioClipHost : IClipHost
    {
        private readonly LongAudioOutput _output;
        private readonly ManualResetEventSlim _finished;
        private readonly ManualResetEventSlim _pastOldEnd;
        private readonly PlaybackEngine _engine;
        public double OldEndMs { get; set; }
        private readonly int _firstAppendedBar;
        private readonly List<(int TrackCount, int[] Bars, string[] AudioFiles)> _uploads = new();
        private DocumentSession? _document;

        public LongAudioClipHost(LongAudioOutput output, PlaybackEngine engine, ManualResetEventSlim finished, ManualResetEventSlim pastOldEnd, int firstAppendedBar)
        {
            _output = output;
            _engine = engine;
            _finished = finished;
            _pastOldEnd = pastOldEnd;
            _firstAppendedBar = firstAppendedBar;
        }

        public AppSettings Settings { get; } = new();
        public AudioClip? SelectedClip { get; set; }
        public int SyncCount { get; private set; }
        public int SyncOutcome { get; private set; } = -2;
        public IReadOnlyList<(int TrackCount, int[] Bars, string[] AudioFiles)> Uploads => _uploads;
        public bool WaitForMarkerOff { get; set; }
        public Action? DuringSync { get; set; }
        public void Attach(DocumentSession document) => _document = document;
        public bool IsShown(DocumentSession document) => ReferenceEquals(document, _document);
        public bool CancelClipDrag() => false;
        public void SetStatus(string text) { }
        public void CheckpointUndo() => DocumentEdits.Checkpoint(_document!);

        public void SyncAudioEngine()
        {
            SyncCount++;
            var project = _document!.Project;
            _uploads.Add((project.Tracks.Count, project.Tracks.Select(t => t.Measures.Count).ToArray(),
                project.Tracks.SelectMany(t => t.AudioClips).Where(c => !c.IsMidi).Select(c => c.File).ToArray()));
            DuringSync?.Invoke();
            if (WaitForMarkerOff)
            {
                if (!_output.MarkerOffBlocked.Wait(TimeSpan.FromSeconds(2)))
                {
                    SyncOutcome = 3;
                    return;
                }
                try
                {
                    _output.ReleaseMarkerOff.Set();
                    var completed = SpinWait.SpinUntil(() =>
                    {
                        if (_finished.IsSet || !_engine.IsPlaying)
                        {
                            _finished.Set();
                            SyncOutcome = 1;
                            return true;
                        }
                        var timeline = _engine.Timeline;
                        if (timeline is not null && timeline.TotalMs > OldEndMs + 1
                            && _engine.Playhead().Bar >= _firstAppendedBar && !_finished.IsSet)
                        {
                            _pastOldEnd.Set();
                            SyncOutcome = 0;
                            return true;
                        }
                        return false;
                    }, TimeSpan.FromMilliseconds(4500));
                    if (!completed) SyncOutcome = 2;
                }
                finally { _output.ReleaseMarkerOff.Set(); }
            }
        }

        public void RefreshTracks() { }
        public void RefreshArrangement() { }
        public void RefreshAfterSongGrew() { }
        public void InvalidateScoreLayout() { }
        public void UpdateTitle() { }
        public void ShowNewTrack(int index) { }
        public bool ShowClipProperties(AudioClip clip) => false;
        public DropItem MeasureDroppedFile(string file, DropItemKind kind, bool transient, MediaContext media) => throw new NotSupportedException();
        public DropItem ReadDroppedMidi(string file, bool transient) => throw new NotSupportedException();
    }

    private sealed class LongAudioFixture : IDisposable
    {
        public required SongProject Project { get; init; }
        public required DocumentSession Document { get; init; }
        public required DocumentSession Sibling { get; init; }
        public required LongAudioOutput Output { get; init; }
        public required PlaybackEngine Engine { get; init; }
        public required LongAudioClipHost Host { get; init; }
        public required ClipEditController Clips { get; init; }
        public required ManualResetEventSlim Finished { get; init; }
        public required ManualResetEventSlim PastOldEnd { get; init; }
        public int TimelineStarts;
        public int TimelineRevisions;

        public void Dispose()
        {
            Output.ReleaseMarkerOff.Set();
            Document.Playback.IsPlayingVisual = false;
            Document.DisposePlayback();
            Sibling.DisposePlayback();
            Finished.Dispose();
            PastOldEnd.Dispose();
            Output.FirstSend.Dispose();
            Output.MarkerOn.Dispose();
            Output.MarkerOffBlocked.Dispose();
            Output.ReleaseMarkerOff.Dispose();
        }
    }

    private static LongAudioFixture LongAudioMakeFixture(bool withMarker)
    {
        var project = SingleTrack(2, 240);
        if (withMarker) Beat(project, 0, 1, 15, 16, LongAudioMarkerPitch);
        project.Tracks.Add(new TrackController().CreateTrack(project, TrackKind.Guitar));
        var output = new LongAudioOutput();
        var engine = new PlaybackEngine(output);
        var document = new DocumentSession(engine) { Project = project };
        var sibling = new DocumentSession { Project = SingleTrack(2, 240) };
        var finished = new ManualResetEventSlim();
        var pastOldEnd = new ManualResetEventSlim();
        var host = new LongAudioClipHost(output, engine, finished, pastOldEnd, project.Tracks.Max(t => t.Measures.Count));
        host.Attach(document);
        var fixture = new LongAudioFixture
        {
            Project = project,
            Document = document,
            Sibling = sibling,
            Output = output,
            Engine = engine,
            Host = host,
            Clips = new ClipEditController(host, new TrackController()),
            Finished = finished,
            PastOldEnd = pastOldEnd
        };
        engine.TimelineChanged += _ => Interlocked.Increment(ref fixture.TimelineStarts);
        engine.TimelineRevised += _ => Interlocked.Increment(ref fixture.TimelineRevisions);
        return fixture;
    }

    private static DropItem LongAudioItem(string name) => new()
    {
        Path = Path.Combine(Path.GetTempPath(), name + ".wav"),
        Name = name,
        Kind = DropItemKind.Audio,
        Seconds = 8
    };

    private static void TestLongAudioClipGrowthPlayback()
    {
        TestLongAudioClipGrowthDuringPlayback();
        TestLongAudioNewTrackWhilePaused();
    }

    private static void TestLongAudioClipGrowthDuringPlayback()
    {
        using var f = LongAudioMakeFixture(withMarker: true);
        var initialTrackCount = f.Project.Tracks.Count;
        var initialBars = f.Project.Tracks.Select(t => t.Measures.Count).ToArray();
        f.Document.Playback.IsPlayingVisual = true;
        f.Engine.Start(f.Project, new PlaybackOptions { Speed = 2, Metronome = false }, _ => { }, f.Finished.Set);
        var started = f.Output.FirstSend.Wait(TimeSpan.FromSeconds(3));
        var sawMarker = started && f.Output.MarkerOn.Wait(TimeSpan.FromSeconds(5));
        var before = f.Engine.Timeline;
        f.Host.OldEndMs = before?.TotalMs ?? 0;
        f.Host.WaitForMarkerOff = true;
        var startsBefore = Volatile.Read(ref f.TimelineStarts);
        var resetsBefore = f.Output.ResetCount;
        var plan = MediaDrop.Plan(f.Project, new[] { LongAudioItem("long-existing") }, 0, 0, 0, SongQuarterMap.For(f.Project));
        f.Clips.ApplyMediaDrop(f.Document, plan);

        var trackBars = f.Project.Tracks.Select(t => t.Measures.Count).ToArray();
        var uploaded = f.Host.Uploads.Count == 1 && f.Host.Uploads[0].AudioFiles.Any(file => file.EndsWith("long-existing.wav", StringComparison.Ordinal));
        var uploadGrewEveryTrack = f.Host.Uploads.Count == 1
            && f.Host.Uploads[0].Bars.Length == initialTrackCount
            && f.Host.Uploads[0].Bars.Zip(initialBars).All(pair => pair.First > pair.Second)
            && f.Host.Uploads[0].Bars.Distinct().Count() == 1;
        Check("long audio playback: edit reaches the last-bar note and sync uploads the new clip after all tracks grow",
            sawMarker && f.Host.SyncCount == 1 && uploaded && uploadGrewEveryTrack && trackBars.Length == initialTrackCount && trackBars.Zip(initialBars).All(pair => pair.First >= pair.Second) && trackBars.Distinct().Count() == 1,
            $"marker={sawMarker}, syncs={f.Host.SyncCount}, uploaded={uploaded}, upload bars={string.Join(',', f.Host.Uploads.FirstOrDefault().Bars ?? Array.Empty<int>())}, bars={string.Join(',', trackBars)}");

        var continued = f.Host.SyncOutcome == 0 && f.Output.MarkerOffWaitReleased && f.Engine.IsPlaying && !f.Finished.IsSet
            && f.Engine.Timeline is { } extended && extended.TotalMs > f.Host.OldEndMs + 1
            && f.Engine.Playhead().Bar >= initialBars.Max();
        var markerAttacks = f.Output.Messages.Count(m => m.Status == 0x90 && m.Pitch == LongAudioMarkerPitch && m.Velocity > 0);
        var noRestart = Volatile.Read(ref f.TimelineStarts) == startsBefore && f.Output.ResetCount == resetsBefore;
        Check("long audio playback: a held audio upload carries the live transport past its old end without restart or replay",
            continued && noRestart && markerAttacks == 1,
            $"sync outcome={f.Host.SyncOutcome} (0=continued, 1=finished, 2=timeout, 3=note-off timeout), starts={startsBefore}->{f.TimelineStarts}, resets={resetsBefore}->{f.Output.ResetCount}, marker attacks={markerAttacks}, revisions={f.TimelineRevisions}, old timeline retained={ReferenceEquals(before, f.Engine.Timeline)}");

        f.Engine.Stop();
        f.Document.Playback.IsPlayingVisual = false;
        var siblingBars = f.Sibling.Project.Tracks[0].Measures.Count;
        var undoSteps = f.Document.Undo.UndoCount;
        var undo = DocumentEdits.Undo(f.Document);
        var restored = undo is not null && f.Document.Project.Tracks.Count == initialTrackCount
            && f.Document.Project.Tracks.All(t => t.Measures.Count == 2 && t.AudioClips.Count == 0);
        var redo = DocumentEdits.Redo(f.Document);
        var reapplied = redo is not null && f.Document.Project.Tracks.Count == initialTrackCount
            && f.Document.Project.Tracks.All(t => t.Measures.Count > 2)
            && f.Document.Project.Tracks.Any(t => t.AudioClips.Any(c => c.File.EndsWith("long-existing.wav", StringComparison.Ordinal)));
        Check("long audio playback: growth, clip drop, one undo and redo stay on the edited document",
            undoSteps == 1 && undo is not null && redo is not null && restored && reapplied && f.Sibling.Project.Tracks[0].Measures.Count == siblingBars && f.Sibling.Project.Tracks[0].AudioClips.Count == 0,
            $"undo steps={undoSteps}, undo={undo is not null}, restored={restored}, redo={redo is not null}, reapplied={reapplied}, sibling bars={f.Sibling.Project.Tracks[0].Measures.Count}");
    }

    private static void TestLongAudioNewTrackWhilePaused()
    {
        using var f = LongAudioMakeFixture(withMarker: false);
        var initialTracks = f.Project.Tracks.Count;
        f.Engine.Start(f.Project, new PlaybackOptions { Speed = 1, Metronome = false }, _ => { }, f.Finished.Set, startPaused: true);
        var ready = f.Output.WaitForResetCount(2, TimeSpan.FromSeconds(3)) && f.Engine.IsPlaying && f.Engine.IsPaused;
        var startsBefore = Volatile.Read(ref f.TimelineStarts);
        var resetsBefore = f.Output.ResetCount;
        var plan = MediaDrop.PlanAddTrackLane(f.Project, new[] { LongAudioItem("long-new-track") }, SongQuarterMap.For(f.Project));
        f.Clips.ApplyMediaDrop(f.Document, plan);

        var bars = f.Project.Tracks.Select(t => t.Measures.Count).ToArray();
        var uploaded = f.Host.SyncCount == 1 && f.Host.Uploads.Count == 1
            && f.Host.Uploads[0].TrackCount == initialTracks + 1
            && f.Host.Uploads[0].Bars.Length == initialTracks + 1
            && f.Host.Uploads[0].Bars.Distinct().Count() == 1
            && f.Host.Uploads[0].Bars.All(count => count > 2)
            && f.Host.Uploads[0].AudioFiles.Any(file => file.EndsWith("long-new-track.wav", StringComparison.Ordinal));
        Check("long audio paused: new audio track and clip are uploaded after every track grows",
            ready && plan.NewTrack && f.Project.Tracks.Count == initialTracks + 1 && uploaded && bars.Distinct().Count() == 1 && bars.All(count => count > 2),
            $"ready={ready}, planned new track={plan.NewTrack}, tracks={f.Project.Tracks.Count}, syncs={f.Host.SyncCount}, bars={string.Join(',', bars)}");
        Check("long audio paused: extending through an audio-only new-track drop does not restart or resume MIDI",
            f.Engine.IsPlaying && f.Engine.IsPaused && Volatile.Read(ref f.TimelineStarts) == startsBefore && f.Output.ResetCount == resetsBefore,
            $"playing={f.Engine.IsPlaying}, paused={f.Engine.IsPaused}, starts={startsBefore}->{f.TimelineStarts}, resets={resetsBefore}->{f.Output.ResetCount}");

        f.Engine.Stop();
        var siblingBars = f.Sibling.Project.Tracks[0].Measures.Count;
        var undoSteps = f.Document.Undo.UndoCount;
        var undo = DocumentEdits.Undo(f.Document);
        var restored = undo is not null && f.Document.Project.Tracks.Count == initialTracks
            && f.Document.Project.Tracks.All(t => t.Measures.Count == 2 && t.AudioClips.Count == 0);
        var redo = DocumentEdits.Redo(f.Document);
        var reapplied = redo is not null && f.Document.Project.Tracks.Count == initialTracks + 1
            && f.Document.Project.Tracks.All(t => t.Measures.Count > 2)
            && f.Document.Project.Tracks[^1].IsAudio
            && f.Document.Project.Tracks[^1].AudioClips.Any(c => c.File.EndsWith("long-new-track.wav", StringComparison.Ordinal));
        Check("long audio paused: new-track growth has one undo and redo and leaves its sibling alone",
            undoSteps == 1 && undo is not null && redo is not null && restored && reapplied && f.Sibling.Project.Tracks[0].Measures.Count == siblingBars && f.Sibling.Project.Tracks[0].AudioClips.Count == 0,
            $"undo steps={undoSteps}, undo={undo is not null}, restored={restored}, redo={redo is not null}, reapplied={reapplied}, sibling bars={f.Sibling.Project.Tracks[0].Measures.Count}");
    }
}
