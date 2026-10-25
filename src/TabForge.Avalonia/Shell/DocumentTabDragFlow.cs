using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using TabForge.Avalonia.Views;
using TabForge.Shell;

namespace TabForge.Avalonia.Shell;

// Owns: a song tab dragged live. Beyond the TabDragRules tear-off band with DetachToNewWindow on, a new window holding the song follows
//   the pointer (DocumentTabLiveTearOff); the source tab is hidden and keeps the capture until release, and the new window is shown without
//   activation so the same gesture keeps steering it. Over another window's strip that strip shows the drop preview and the dragged window
//   turns translucent; within its own strip the tab reorders with its neighbours sliding. Release merges the song into the strip under the
//   pointer at the insertion slot (MergeAcrossWindows), gives it back to its own strip when that is under the pointer, or keeps the new
//   window where it is and activates it. Escape gives the song back to the source window.
// Does not own: the tab order and the songs (MainWindowTabs), the press gesture (TabStripView), the window list (TabWindowRegistry).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests, TabForge.Avalonia.Tests.TabDragRulesTests.
/// <summary>Document tab tear-off, reorder and cross-window moves for one main window. UI thread only.</summary>
public sealed class DocumentTabDragFlow
{
    private readonly MainWindow _window;
    private readonly MainWindowTabs _tabs;
    private readonly TabWindowRegistry _registry;
    private readonly TabStripView _strip;
    private readonly DragSession<int> _drag;
    private DocumentTabLiveTearOff? _tear;
    private MainWindow? _previewWindow;   // the other window whose strip shows the drop preview
    private int _ownSlot = -1;            // the in-strip insertion slot being previewed; -1 when none

    public DocumentTabDragFlow(MainWindow window, MainWindowTabs tabs, TabStripView strip, TabWindowRegistry registry)
    {
        _window = window;
        _tabs = tabs;
        _strip = strip;
        _registry = registry;
        _drag = new DragSession<int>(EndPreview);
        strip.TabDragMoved += (index, e) => Move(index, strip.PointToScreen(e.GetPosition(strip)));
        strip.TabDragEnded += (index, e) => Drop(index, strip.PointToScreen(e.GetPosition(strip)));
        strip.TabDragCancelled += Cancel;
        // A live tear-off keeps its drag when another window takes activation; a drag in the strip ends with the window.
        strip.TabDragInterrupted += () => { if (_tear is null) Cancel(); };
        window.AddHandler(InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public bool IsDragging => _drag.IsDragging;

    public void Move(int index, PixelPoint screen)
    {
        if (index >= _tabs.Songs.Count || !_drag.TryFollow(index)) return;
        if (_tear is null && CanDetach(screen)) StartTear(index, screen);
        _tear?.Follow(screen);
        UpdatePreview(index, screen);
    }

    public void Drop(int index, PixelPoint screen)
    {
        var tear = _tear;
        // Read before the drag ends: ending it eases the tab back to its place.
        var carried = _strip.DraggedTabOffset;
        if (!_drag.Drop(index)) return;   // Escape gave the song back already
        _tear = null;
        var target = _registry.StripWindowAt(screen, tear?.Window ?? _window);
        if (target == _window)
        {
            tear?.Revert();   // back into its own strip: the song never left the source window
            ReorderOwnTab(index, screen, carried);
        }
        else if (target is not null && _strip.Settings.MergeAcrossWindows) MergeInto(index, tear, target, screen);
        else if (tear is { IsLone: false })
        {
            _tabs.Detach(index);          // the new window keeps the song
            tear.Window.Songs.SetTearCopy(false);
            tear.Window.Activate();        // and receives focus only now, when the gesture is over
        }
        else if (tear is null && CanDetach(screen)) TearOff(index, screen);
        else if (tear is null) ReorderOwnTab(index, screen, carried);
    }

    // The settings are read here, not cached, so a Preferences change applies to the next drag.
    private bool CanDetach(PixelPoint screen) => _strip.Settings.DetachToNewWindow && IsBeyondTearOff(screen);

    /// <summary>Gives the song back to the source window when a drag tore it off, then stops the drag.</summary>
    public void Cancel()
    {
        RevertTear();
        _drag.Cancel();
    }

    /// <summary>Moves the song at <paramref name="index"/> into a new window at the screen point, or moves this window there
    /// when it is the only tab. Returns the window that now holds the song.</summary>
    public MainWindow TearOff(int index, PixelPoint screen)
    {
        if (TabDragRules.TearOffAction(_tabs.Songs.Count) == TabTearOffAction.MoveWindow)
        {
            _window.Position = screen;
            return _window;
        }
        var torn = new MainWindow(_window.Settings, _registry, _tabs.Detach(index), _window.Playback) { Position = screen, RequestedThemeVariant = _window.RequestedThemeVariant };
        torn.Show();
        return torn;
    }

    /// <summary>Moves the song at <paramref name="index"/> into <paramref name="target"/>'s tabs at <paramref name="slot"/>; this window
    /// closes when it was its last song.</summary>
    public void MoveTo(int index, MainWindow target, int slot)
    {
        target.Songs.InsertAt(_tabs.Detach(index), slot);
        target.Activate();
        if (_tabs.Songs.Count == 0) _window.Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && _drag.IsDragging) RevertTear();
        _drag.OnKeyDown(sender, e);
    }

    private void RevertTear()
    {
        _tear?.Revert();
        _tear = null;
    }

    private void StartTear(int index, PixelPoint screen)
    {
        ClearOwnPreview();
        // Record the tear before showing the window: a deactivation from Show then cannot look like a drag with no tear and cancel it.
        _tear = DocumentTabLiveTearOff.Create(_window, index, screen);
        if (!_tear.IsLone) _strip.SetHiddenTab(index);
        _tear.Show();
    }

    // Shows where a release would drop the song: another window's strip (highlight and insertion marker there, the dragged window
    // translucent), or this strip (an insertion marker and, while the song is not torn off, the neighbours sliding into the gap).
    private void UpdatePreview(int index, PixelPoint screen)
    {
        var other = _strip.Settings.MergeAcrossWindows ? _registry.StripWindowAt(screen, _tear?.Window ?? _window) : null;
        if (other == _window) other = null;   // a torn song over its own strip is a pop-back, shown on this strip
        _tear?.SetOverTarget(other is not null);
        if (other is not null) ShowOtherPreview(other, screen);
        else if (OverOwnStrip(screen)) ShowOwnPreview(index, screen);
        else
        {
            ClearOtherPreview();
            ClearOwnPreview();
        }
    }

    private void ShowOtherPreview(MainWindow other, PixelPoint screen)
    {
        if (_previewWindow != other) ClearOtherPreview();
        ClearOwnPreview();
        _previewWindow = other;
        other.TabStrip.ShowDropPreview(other.TabStrip.InsertionSlot(screen.X), highlightStrip: true);
    }

    private void ShowOwnPreview(int index, PixelPoint screen)
    {
        ClearOtherPreview();
        var slot = _strip.InsertionSlot(screen.X, index);
        _strip.ShowDropPreview(slot, highlightStrip: false);
        if (slot == _ownSlot) return;
        _ownSlot = slot;
        if (_tear is not null) return;   // the torn song is not in this strip's order any more
        _strip.SlideForReorder(index, slot, AnimateSlides);
    }

    private void ClearOtherPreview()
    {
        _previewWindow?.TabStrip.HideDropPreview();
        _previewWindow = null;
    }

    private void ClearOwnPreview()
    {
        if (_ownSlot < 0) return;
        _ownSlot = -1;
        _strip.HideDropPreview();
        _strip.ClearSlides();
    }

    // Runs on every end of a drag (release, Escape, lost capture). The markers are hidden unconditionally, so none is left showing.
    private void EndPreview()
    {
        _strip.SetHiddenTab(null);
        _strip.HideDropPreview();
        _strip.ClearSlides();
        _strip.EndDrag();
        _ownSlot = -1;
        ClearOtherPreview();
        _tear?.SetOverTarget(false);
    }

    private void MergeInto(int index, DocumentTabLiveTearOff? tear, MainWindow target, PixelPoint screen)
    {
        var slot = target.TabStrip.InsertionSlot(screen.X);
        tear?.CloseNewWindow();
        MoveTo(index, target, slot);
    }

    // The released tab keeps its offset from its old place, so the strip rebuilt at the new order eases it into its new slot.
    private void ReorderOwnTab(int index, PixelPoint screen, double carried)
    {
        var slot = _strip.InsertionSlot(screen.X, index);
        var destination = TabDragRules.ReorderDestination(index, slot, _tabs.Songs.Count);
        if (destination < 0 || destination == index) return;
        var displacement = _strip.ReorderDisplacement(index, destination);
        _strip.CarryDraggedTab(destination, carried - displacement);
        _tabs.Reorder(index, destination);
    }

    private bool OverOwnStrip(PixelPoint screen) => _strip.ContainsScreenPoint(screen);

    private bool AnimateSlides => _window.Settings?.Settings.Appearance.ReduceAnimations != true;

    private bool IsBeyondTearOff(PixelPoint screen)
    {
        var point = _window.PointToClient(screen);
        var stripTop = _window.TabStrip.TranslatePoint(default, _window)?.Y ?? 0;
        return TabDragRules.IsBeyondTearOff(point.X, point.Y, _window.ClientSize.Width, stripTop, _window.TabStrip.Bounds.Height);
    }
}
