#pragma warning disable CS0618 // Avalonia 11.3 marks the IDataObject drag API obsolete; it still carries an in-app drag.
using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Avalonia.Features.LeftStrip;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the window side of the Composer drum samples: auditioning one on a loop at the song's tempo through the test-note engine (one sample at a
//   time, the panel told of every step and of the stop), and the drag onto the arrangement as a real drag and drop (an in-app data format, the same
//   lane highlight and landing ghost as an audio-file drop), the Tab/MIDI lane prompt with its remembered choice, and the post-edit refresh.
// Does not own: the samples (DrumSamples), the panel and its rows (ComposerPanel), the landing edit (DrumSampleDrop), the prompt window
//   (DrumSampleLanePromptWindow), or audio-file drops (MainWindowAudioDrop, which leaves a drag with no files alone).
// Tests: TabForge.Avalonia.Tests.DrumSampleDropTests (the landing rules); the drag and the audition are UI-only.
internal sealed class MainWindowComposerDrums
{
    /// <summary>The drag data format: the dragged sample's id.</summary>
    internal const string DataFormat = "TabForge.ComposerDrumSample";

    private readonly Window _owner;
    private readonly MainWindowDocumentPanes _panes;
    private readonly MainWindowTabs _songs;
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly MainWindowSettings? _settings;
    private readonly Action<string> _status;
    private readonly ScorePreviewView _score;

    private DispatcherTimer? _timer;
    private DrumSample? _playing;
    private Action<int>? _onStep;
    private int _step;
    private (TrackModel? Track, bool NewTrack, int Bar)? _hovered;

    private MainWindowComposerDrums(Window owner, MainWindowDocumentPanes panes, MainWindowTabs songs, AvaloniaPlaybackSessions? playback,
        MainWindowSettings? settings, Action<string> status, ScorePreviewView score)
    {
        _owner = owner;
        _panes = panes;
        _songs = songs;
        _playback = playback;
        _settings = settings;
        _status = status;
        _score = score;
    }

    /// <summary>Hooks the existing arrangement and every later one for sample drops, and returns the hooks the Composer panel calls.</summary>
    public static ComposerDrumHooks Register(Window owner, MainWindowDocumentPanes panes, MainWindowTabs songs, AvaloniaPlaybackSessions? playback,
        MainWindowSettings? settings, Action<string> status, ScorePreviewView score)
    {
        var drums = new MainWindowComposerDrums(owner, panes, songs, playback, settings, status, score);
        if (panes.Arrangement is { } existing) drums.Attach(existing);
        panes.ArrangementCreated += drums.Attach;
        return new ComposerDrumHooks { Play = drums.Play, Stop = drums.Stop, Drag = drums.Drag };
    }

    // ---------- audition ----------

    private int Tempo => Math.Max(30, _songs.Active.Project.Tempo);

    private void Play(DrumSample sample, Action<int> onStep)
    {
        Stop();
        _playing = sample;
        _onStep = onStep;
        _step = 0;
        _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = StepInterval() };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
        Tick();
    }

    private TimeSpan StepInterval() => TimeSpan.FromSeconds(60.0 / Tempo / 4);

    private void Tick()
    {
        if (_playing is not { } sample || _timer is null) return;
        _timer.Interval = StepInterval();   // follows a tempo change while auditioning
        var step = _step % DrumSample.StepsPerBar;
        var lengthMs = Math.Max(30, (int)StepInterval().TotalMilliseconds);
        if (_playback is not null)
            foreach (var midi in sample.NotesAt(step))
                UiTasks.Forget(_playback.PlayTestNoteAsync(-1, 9, 0, midi, lengthMs), "Drum audition", _status);
        _onStep?.Invoke(step);
        _step++;
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
        _playing = null;
        var onStep = _onStep;
        _onStep = null;
        onStep?.Invoke(-1);
    }

    // ---------- drag ----------

    private void Drag(DrumSample sample, PointerEventArgs trigger)
    {
        Stop();
        if (_panes.Arrangement is not { IsVisible: true })
        {
            _status.Warning("Show the timeline to drop a drum groove on a track");
            return;
        }
        UiTasks.Forget(DragAsync(sample, trigger), "Dragging the drum groove", _status);
    }

    private async Task DragAsync(DrumSample sample, PointerEventArgs trigger)
    {
        var data = new DataObject();
        data.Set(DataFormat, sample.Id);
        _status($"Drop '{sample.Name}' on a track at the bar to start from (Esc cancels)");
        var effect = await DragDrop.DoDragDrop(trigger, data, DragDropEffects.Copy);
        if (effect == DragDropEffects.None) _status("Drag cancelled: nothing was added");
    }

    private static DrumSample? SampleOf(DragEventArgs e) =>
        e.Data.Contains(DataFormat) && e.Data.Get(DataFormat) is string id ? DrumSamples.ById(id) : null;

    private void Attach(ArrangementView arrangement)
    {
        DragDrop.SetAllowDrop(arrangement, true);
        arrangement.AddHandler(DragDrop.DragEnterEvent, new EventHandler<DragEventArgs>((_, e) => OnDragOver(arrangement, e)));
        arrangement.AddHandler(DragDrop.DragOverEvent, new EventHandler<DragEventArgs>((_, e) => OnDragOver(arrangement, e)));
        arrangement.AddHandler(DragDrop.DragLeaveEvent, new EventHandler<DragEventArgs>((_, e) =>
        {
            if (SampleOf(e) is null) return;
            ClearPreview(arrangement);
            e.Handled = true;
        }));
        arrangement.AddHandler(DragDrop.DropEvent, new EventHandler<DragEventArgs>((_, e) => OnDrop(arrangement, e)));
    }

    private void OnDragOver(ArrangementView arrangement, DragEventArgs e)
    {
        if (SampleOf(e) is not { } sample) return;
        e.Handled = true;
        var hit = Hit(arrangement, e);
        if (hit is not { } target || !ReferenceEquals(arrangement.Timeline.Song, _songs.Active.Project))
        {
            e.DragEffects = DragDropEffects.None;
            ClearPreview(arrangement);
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        if (_hovered == target) return;
        _hovered = target;
        Preview(arrangement, sample, target.Track, target.NewTrack, target.Bar);
        _status(target.NewTrack
            ? $"Drop '{sample.Name}' at bar {target.Bar + 1} on a new drum track"
            : $"Drop '{sample.Name}' at bar {target.Bar + 1} on '{target.Track!.Name}'");
    }

    private void OnDrop(ArrangementView arrangement, DragEventArgs e)
    {
        if (SampleOf(e) is not { } sample) return;
        e.Handled = true;
        var hit = Hit(arrangement, e);
        ClearPreview(arrangement);
        if (hit is not { } target)
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }
        e.DragEffects = DragDropEffects.Copy;
        UiTasks.Forget(DropAsync(sample, arrangement, target.Track, target.NewTrack, target.Bar), "Adding the drum groove", _status);
    }

    /// <summary>The track (or the Add track strip, a new drum track) and the bar under the pointer; null off the rows.</summary>
    private static (TrackModel? Track, bool NewTrack, int Bar)? Hit(ArrangementView arrangement, DragEventArgs e)
    {
        var song = arrangement.Timeline.Song;
        var timeline = arrangement.Timeline;
        var strip = arrangement.AddTrackBar;
        if (strip.IsVisible)
        {
            var inStrip = e.GetPosition(strip);
            if (inStrip.X >= 0 && inStrip.Y >= 0 && inStrip.X <= strip.Bounds.Width && inStrip.Y <= strip.Bounds.Height)
                return (null, true, BarAt(arrangement, e));
        }
        var point = e.GetPosition(timeline);
        if (point.X < 0 || point.Y < 0 || point.X > timeline.Bounds.Width || point.Y > timeline.Bounds.Height) return null;
        var row = timeline.RowAtY(point.Y);
        if (row < 0 || row >= song.Tracks.Count) return null;
        return (song.Tracks[row], false, BarAt(arrangement, e));
    }

    private static int BarAt(ArrangementView arrangement, DragEventArgs e)
    {
        var bars = AudioClipTimelineRules.BarCount(arrangement.Timeline.Song);
        if (bars == 0) return 0;
        return Math.Clamp(arrangement.Timeline.BarAtX(e.GetPosition(arrangement.Timeline).X), 0, bars - 1);
    }

    private static void Preview(ArrangementView arrangement, DrumSample sample, TrackModel? track, bool newTrack, int bar)
    {
        var song = arrangement.Timeline.Song;
        var target = newTrack ? AudioDropTarget.NewTrack : AudioDropTarget.On(track!);
        var startSec = AudioClipTimelineRules.SecondsAtBar(song, bar, 0);
        var span = EndSec(song, bar, sample.Bars) - startSec;
        var planned = AudioDropPreviewPlanner.Plan(song, target, startSec, new[] { (sample.Name, span) });
        // The ghost spans the groove's bars on the written grid, on a track and on the Add track strip alike (the landed drop is bar-exact).
        var preview = planned is { } p ? p with { BarSpan = (bar, sample.Bars) } : null;
        arrangement.AddTrackBar.DropActive = newTrack;
        arrangement.Timeline.FileDropPreview = preview;
        arrangement.AddTrackBar.DropGhost = preview is { Track: null } ghost ? MainWindowAudioDrop.StripGhost(arrangement, ghost) : null;
        arrangement.Timeline.InvalidateVisual();
    }

    /// <summary>The second the sample's last bar ends; past the song's end the last bar's length repeats.</summary>
    private static double EndSec(SongProject song, int bar, int bars)
    {
        var count = AudioClipTimelineRules.BarCount(song);
        if (bar + bars <= count) return AudioClipTimelineRules.SecondsAtBar(song, bar + bars, 0);
        var start = AudioClipTimelineRules.SecondsAtBar(song, bar, 0);
        var barDur = count > bar ? AudioClipTimelineRules.SecondsAtBar(song, bar + 1, 0) - start : 60.0 / Math.Max(30, song.Tempo) * 4;
        return start + barDur * bars;
    }

    private void ClearPreview(ArrangementView arrangement)
    {
        _hovered = null;
        arrangement.AddTrackBar.DropActive = false;
        arrangement.Timeline.FileDropPreview = null;
        arrangement.AddTrackBar.DropGhost = null;
        arrangement.Timeline.InvalidateVisual();
    }

    // ---------- drop ----------

    private async Task DropAsync(DrumSample sample, ArrangementView arrangement, TrackModel? track, bool newTrack, int bar)
    {
        var song = _songs.Active;
        if (!ReferenceEquals(arrangement.Timeline.Song, song.Project)) return;
        var tabAvailable = track?.HasNotation ?? true;
        var remembered = DrumSampleDropChoice.Normalize(_settings?.Settings.Editing.ComposerDrumDropLane);
        DrumSampleLane lane;
        if (remembered == DrumSampleDropChoice.Midi || (remembered == DrumSampleDropChoice.Tab && !tabAvailable)) lane = DrumSampleLane.Midi;
        else if (remembered == DrumSampleDropChoice.Tab) lane = DrumSampleLane.Tab;
        else
        {
            var answer = await DrumSampleLanePromptWindow.AskAsync(_owner,
                new DrumSampleDropQuestion(sample.Name, track?.Name ?? "a new drum track", bar + 1, sample.Bars, tabAvailable));
            if (answer is null)
            {
                _status("Cancelled: nothing was added");
                return;
            }
            if (answer.Value.Remember && _settings is not null)
            {
                _settings.Settings.Editing.ComposerDrumDropLane = DrumSampleDropChoice.Store(answer.Value);
                _settings.SaveSettings();
            }
            lane = answer.Value.Lane;
        }
        // The prompt may have stayed open while the tab or the song changed.
        if (!ReferenceEquals(_songs.Active, song) || !ReferenceEquals(arrangement.Timeline.Song, song.Project)) return;
        var result = DrumSampleDrop.Apply(song, sample, track, newTrack, bar, lane);
        if (result is null)
        {
            _status.Warning($"Could not add '{sample.Name}' there");
            return;
        }
        if (result.Clip is { } clip) arrangement.Timeline.SelectedClip = new TimelineClipSelection(result.Track, clip);
        arrangement.Timeline.ClipEditCommitted(song, result.Clip is { } landed ? new[] { landed } : null);
        if (newTrack) arrangement.ShowTrack(result.Track);
        _score.Refresh();
        _status(result.Status);
    }
}
#pragma warning restore CS0618
