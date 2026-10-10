using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Models;

namespace TabForge.Views;

// Owns: the track row's menu and keyboard actions: the row menu request, row focus, rename start, track colour, and the right
//   click on a row background (RightClickTrackRow).
// Does not own: the menu items and the track edits (TrackController.cs).
// Tests: TestTrackRowRightClick.

public sealed partial class ArrangementPanel
{
    /// <summary>Right-click on a track row's background (track index): the host opens the row menu. Without a subscriber the row keeps its
    /// older behaviour (Track properties, or the audio row menu).</summary>
    public event EventHandler<int>? TrackRowMenuRequested;

    private readonly Dictionary<TrackModel, Action> _renameStarters = new();

    /// <summary>True while a track row (or a control inside it) has the keyboard focus.</summary>
    public bool TrackRowFocused => _trackRows.Any(row => row.IsKeyboardFocusWithin);

    /// <summary>Puts the keyboard focus on the row of a track once the pending rebuild is done.</summary>
    internal void FocusTrackRow(int index) =>
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            if (index >= 0 && index < _trackRows.Count) _trackRows[index].Focus();
        }));

    /// <summary>The row's name box in edit mode (the menu's Rename).</summary>
    internal void BeginTrackRename(int index)
    {
        if (_project is { } project && index >= 0 && index < project.Tracks.Count && _renameStarters.TryGetValue(project.Tracks[index], out var begin)) begin();
    }

    /// <summary>The row colour menu's choice, from the row menu's Colour submenu.</summary>
    internal void ApplyTrackColour(int index, string hex)
    {
        TrackEditRequested?.Invoke(new TrackEditRequest(index, TrackEditKind.SetColor, hex));
        TrackColorChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Right-clicks the row background as the pointer would (test seam); true when the host was asked for the menu.</summary>
    internal bool RightClickTrackRow(int index)
    {
        if (index < 0 || index >= _trackRows.Count) return false;
        var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Right)
        {
            RoutedEvent = UIElement.PreviewMouseRightButtonUpEvent, Source = _trackRows[index]
        };
        _trackRows[index].RaiseEvent(args);
        return args.Handled;
    }
}
