using System;
using TabForge.Avalonia.Features.Recording.View;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Avalonia.Views;

// Owns: the arrangement's track-row stack: the row source that watches a song and reloads the rows, the armed sub-row layer, the row scroll bar,
//   the timeline subscriptions that keep them in step (rows changed, row scrolled, layout), the pane's natural maximum height and the selected-row
//   reload. It is the plan's TrackRowCoordinator, named for the row stack it is.
// Does not own: the timeline/header drawing, the input controller (TimelineInputController), the top-band layout (ArrangementTopBand), the window's
//   playback/wheel/splitter behaviour or the track edits themselves — those stay in ArrangementView or the classes that own them.
// Tests: TabForge.Avalonia.Tests.ArrangementViewTests, TabForge.Avalonia.Tests.TrackListScrollTests, TabForge.Avalonia.Tests.TrackRowsRegrowTests.
internal sealed class ArrangementTrackRows
{
    private readonly TimelineView _timeline;
    private readonly TrackHeaderColumn _header;
    private readonly Func<double> _naturalHeight;
    private readonly Action<double> _setMaxHeight;
    private IEditableDocument _document;
    private bool _watchingTimeline;
    private EventHandler? _layoutHandler;

    /// <summary>Builds the three row controls; the view subscribes their events at the same points its constructor used to.</summary>
    public ArrangementTrackRows(TimelineView timeline, TrackHeaderColumn header, IEditableDocument document, Func<double> naturalHeight, Action<double> setMaxHeight, SharedMeterTick? meterTick = null)
    {
        _timeline = timeline;
        _header = header;
        _document = document;
        _naturalHeight = naturalHeight;
        _setMaxHeight = setMaxHeight;
        Source = new ArrangementRowSource(timeline, header);
        Source.RowsCountChanged += () => RowsCountChanged?.Invoke();
        Armed = new ArmedRowLayer(timeline, header, document, AvaloniaUiThread.ForApplication(), meterTick, TabForge.Audio.MidiInputCapture.DeviceNames);
        Bar = new TrackRowScrollBar(timeline);
    }

    /// <summary>The rows the header and timeline show, reloaded when the song's tracks change.</summary>
    public ArrangementRowSource Source { get; }

    /// <summary>The armed tracks' sub-rows (monitor, input) and their level poll.</summary>
    public ArmedRowLayer Armed { get; }

    /// <summary>The vertical scroll bar beside the track list; visible only while the rows overflow the pane.</summary>
    public TrackRowScrollBar Bar { get; }

    /// <summary>Raised when a track or group row is added or removed, or a row's height changes; the view re-raises its public event.</summary>
    public event Action? RowsCountChanged;

    /// <summary>Keeps the armed sub-rows in step with the timeline's rows and its scroll offset (the same subscriptions the view had).</summary>
    public void WatchTimeline()
    {
        if (_watchingTimeline) return;
        _watchingTimeline = true;
        _timeline.RowsChanged += Armed.Sync;
        _timeline.RowScrollChanged += Armed.Sync;
    }

    /// <summary>Runs the natural-height fit and the armed-row sync from the timeline's layout pass; called at the view's old subscription point.</summary>
    public void WatchLayout()
    {
        if (_layoutHandler is not null) return;
        _layoutHandler = (_, _) => { UpdateNaturalMaxHeight(); Armed.Sync(); };
        _timeline.LayoutUpdated += _layoutHandler;
    }

    /// <summary>Starts watching <paramref name="document"/>'s rows (when the pane is attached).</summary>
    public void Watch(IEditableDocument document)
    {
        WatchTimeline();
        WatchLayout();
        Source.Watch(document);
    }

    /// <summary>Stops watching the rows and the timeline's events when the pane is detached (a pane kept after its window closes holds no reference to it).</summary>
    public void Unwatch()
    {
        Source.Unwatch();
        if (_watchingTimeline)
        {
            _watchingTimeline = false;
            _timeline.RowsChanged -= Armed.Sync;
            _timeline.RowScrollChanged -= Armed.Sync;
        }
        if (_layoutHandler is { } layout) { _timeline.LayoutUpdated -= layout; _layoutHandler = null; }
    }

    /// <summary>Reloads the rows after an edit the timeline does not mark (an armed sub-row change).</summary>
    public void ReloadRows() => Source.Reload(_document.Project);

    /// <summary>Shows and edits <paramref name="document"/> from now on (the armed rows follow the same document).</summary>
    public void Show(IEditableDocument document)
    {
        _document = document;
        Armed.Show(document);
    }

    /// <summary>Hands the document's selected track to the timeline, which redraws only when it changed; runs with each playhead update and after a track is added. True when the row changed.</summary>
    public bool SyncSelectedRow()
    {
        var row = _document is SongDocument song ? song.TrackIndex : -1;
        if (row == _timeline.SelectedRow) return false;
        _timeline.SelectedRow = row;
        _timeline.EnsureRowVisible(row);
        _header.InvalidateVisual();
        return true;
    }

    // The dock's splitter stops at the natural height, so the pane takes no empty space below the rows. Written only when it changes, so the layout settles.
    private void UpdateNaturalMaxHeight() => _setMaxHeight(_naturalHeight());
}
