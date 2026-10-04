using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

// ArrangementPanel: the Add-track lane forwarders (AddLaneController owns the lane) and the audio-track row parts
// (waveform label instead of the instrument picker, the row's right-click menu with "Convert to instrument track").
public sealed partial class ArrangementPanel : IAddLaneHost
{
    /// <summary>The Add-track lane was clicked (either half): the host asks Audio or Instrument.</summary>
    public event Action? AddTrackLaneClicked;
    /// <summary>"Convert to instrument track" on an audio track's row (track index).</summary>
    public event Action<int>? ConvertAudioTrackRequested;

    /// <summary>The strip labelled "Add track" under the last track (Preferences > Timeline &amp; Tracks). The + Track button stays either way.</summary>
    public bool ShowAddTrackLane { get => _addLane.Shown; set => _addLane.Shown = value; }

    /// <summary>Actual height of the lane: all the room left below the last row in the pane (at least one row); 0 when hidden.</summary>
    internal double AddLaneFillHeight => _addLane.FillHeight;

    /// <summary>The track-list half of the lane (null when hidden): test and render hook.</summary>
    internal FrameworkElement? AddLaneRow => _addLane.Row;

    /// <summary>Clicks the lane as the pointer would (test seam).</summary>
    internal void ClickAddLane() => AddTrackLaneClicked?.Invoke();

    /// <summary>Shows the zone hovered and/or dragged over without a pointer (test and render seam).</summary>
    internal void SimulateAddLaneState(bool hot, bool drag) => _addLane.Simulate(hot, drag);

    /// <summary>The list's half as it looks now: "idle", "hover" or "drag" (test seam).</summary>
    internal string AddLaneState => _addLane.State;

    SongProject? IAddLaneHost.Project => _project;
    double IAddLaneHost.ControlsViewportHeight => _controlsScroll.ActualHeight;
    double IAddLaneHost.TimelineHorizontalOffset => _horizontal.HorizontalOffset;
    void IAddLaneHost.RebuildControls() => RebuildControls();
    void IAddLaneHost.RefreshTimelineExtent() => RefreshTimelineExtent();
    void IAddLaneHost.RaiseAddTrackLaneClicked() => AddTrackLaneClicked?.Invoke();

    /// <summary>Right-click on an audio track's row: its properties, or turning it into an instrument track.</summary>
    private void ShowAudioRowMenu(int index, FrameworkElement target)
    {
        var menu = AudioRowMenu(index, target);
        menu.IsOpen = true;
    }

    internal ContextMenu AudioRowMenu(int index, FrameworkElement target)
    {
        var menu = new ContextMenu { Style = (Style)Application.Current.FindResource(typeof(ContextMenu)), PlacementTarget = target };
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header, Style = (Style)Application.Current.FindResource(typeof(MenuItem)) };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }
        Item("Track properties…", () => TrackOptionsRequested?.Invoke(this, index));
        Item("Convert to instrument track…", () => ConvertAudioTrackRequested?.Invoke(index))
            .ToolTip = "Choose an instrument; the track keeps its audio and MIDI clips, moved to a second lane below the new tab lane";
        return menu;
    }
}
