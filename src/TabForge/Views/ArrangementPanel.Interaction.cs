using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using TabForge.Controllers;
using TabForge.Diagnostics;
using TabForge.Models;
using TabForge.Playback;
using TabForge.Plugins;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Views;

// ArrangementPanel: track-row drag to reorder and inline track-name editing.
public sealed partial class ArrangementPanel
{
    private readonly List<Border> _trackRows = new();
    private readonly List<TranslateTransform> _rowTransforms = new();
    private BitmapCache? _rowDragCache;
    private TextBox? _editingTrackName;
    private TrackModel? _editingTrackModel;
    private int _selectedTrackIndex = -1;
    private int _dragFromTrack = -1;
    private int _dragTargetTrack = -1;
    private bool _dragArmed;
    private Point _dragOrigin;
    private Border? _dragCaptureRow;
    private System.Windows.Media.Effects.DropShadowEffect? _dragShadow;

    /// <summary>Track index and number; the containing row owns the drag gesture.</summary>
    private UIElement BuildTrackHandle(int index, TrackModel track)
    {
        var muted = (Brush)Application.Current.FindResource("MutedBrush");
        var handle = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Background = Brushes.Transparent,
            Cursor = Cursors.Arrow,
            ToolTip = "Drag up or down to reorder this track"
        };
        var number = new TextBlock
        {
            Text = (index + 1).ToString(), FontSize = 11, Foreground = muted,
            VerticalAlignment = VerticalAlignment.Center,
            // Clicking the number selects the track; show a hand instead of the drag cursor.
            Cursor = Cursors.Arrow,
            ToolTip = "Click to select, drag up or down to reorder"
        };
        handle.Children.Add(number);

        return handle;
    }

    private void BeginControlTrackDrag(int index, Border row, Point pointer)
    {
        _dragFromTrack = index;
        _dragTargetTrack = index;
        _dragArmed = false;
        _dragOrigin = pointer;
        _dragCaptureRow = row;
        row.CaptureMouse();
        TrackSelected?.Invoke(this, index);
    }

    private void UpdateControlTrackDrag(int index, MouseEventArgs e)
    {
        if (_dragFromTrack != index || e.LeftButton != MouseButtonState.Pressed) return;
        var viewportPoint = e.GetPosition(_controlsScroll);
        if (viewportPoint.Y < 14 && _controlsScroll.VerticalOffset > 0)
            _controlsScroll.ScrollToVerticalOffset(Math.Max(0, _controlsScroll.VerticalOffset - TrackRowHeight / 2));
        else if (viewportPoint.Y > _controlsScroll.ViewportHeight - 14 && _controlsScroll.VerticalOffset < _controlsScroll.ScrollableHeight)
            _controlsScroll.ScrollToVerticalOffset(Math.Min(_controlsScroll.ScrollableHeight, _controlsScroll.VerticalOffset + TrackRowHeight / 2));
        var pointer = e.GetPosition(_controls);
        if (!_dragArmed && Math.Abs(pointer.Y - _dragOrigin.Y) < 3) return;
        if (!_dragArmed) TrackDragStarted?.Invoke(this, EventArgs.Empty);
        _dragArmed = true;
        _dragTargetTrack = TrackIndexAtY(pointer.Y);
        UpdateDragVisual(pointer);
        e.Handled = true;
    }

    private void EndControlTrackDrag(Border row, MouseButtonEventArgs e)
    {
        var from = _dragFromTrack;
        var to = _dragTargetTrack;
        var armed = _dragArmed;
        _dragFromTrack = -1;
        _dragTargetTrack = -1;
        _dragArmed = false;
        _dragCaptureRow = null;
        row.ReleaseMouseCapture();
        UpdateDragVisual();
        if (armed && from >= 0 && to >= 0 && to != from) TrackReordered?.Invoke(this, (from, to));
        e.Handled = true;
    }

    private void CancelControlTrackDrag(Border row)
    {
        if (_dragCaptureRow != row) return;
        _dragCaptureRow = null;
        _dragFromTrack = -1;
        _dragTargetTrack = -1;
        _dragArmed = false;
        UpdateDragVisual();
    }

    private static bool IsInside(DependencyObject? element, DependencyObject ancestor)
    {
        while (element is not null)
        {
            if (ReferenceEquals(element, ancestor)) return true;
            element = VisualTreeHelper.GetParent(element);
        }
        return false;
    }

    private static bool IsInteractiveTrackControl(DependencyObject? element, Border row)
    {
        while (element is not null && !ReferenceEquals(element, row))
        {
            if (element is ButtonBase or Slider or ComboBox or ComboBoxItem or KnobControl or Thumb or FxSplitButton or RecordArmButton or MonitorButton) return true;
            if (element is TextBox textBox && !textBox.IsReadOnly) return true;
            // Dropdown items live in a popup: its visual tree ends at the popup root, not the row,
            // but the click still routes here. Treat anything inside a popup as interactive.
            var parent = VisualTreeHelper.GetParent(element);
            if (parent is null && element is FrameworkElement { Parent: System.Windows.Controls.Primitives.Popup }) return true;
            if (parent is null && element.GetType().Name == "PopupRoot") return true;
            element = parent;
        }
        return false;
    }

    private void FinishTrackNameEdit(TextBox name, TrackModel track, bool commit)
    {
        var changed = commit && !string.Equals(track.Name, name.Text, StringComparison.Ordinal);
        if (changed && _project is not null)
            TrackEditRequested?.Invoke(new TrackEditRequest(_project.Tracks.IndexOf(track), TrackEditKind.Rename, name.Text));
        else if (!commit) name.Text = track.Name;
        name.IsReadOnly = true;
        name.Cursor = Cursors.Arrow;
        name.Background = Brushes.Transparent;
        name.BorderBrush = Brushes.Transparent;
        name.BorderThickness = new Thickness(0);
        // Drop the selection highlight and keyboard focus so no focus border or caret is left behind;
        // a non-focusable idle name also can't be re-focused by stray clicks or Tab.
        name.Select(0, 0);
        name.Focusable = false;
        name.HorizontalAlignment = HorizontalAlignment.Left;
        if (name.IsKeyboardFocusWithin) Keyboard.ClearFocus();
        if (ReferenceEquals(_editingTrackName, name))
        {
            _editingTrackName = null;
            _editingTrackModel = null;
        }
        if (changed) ProjectEdited?.Invoke(this, EventArgs.Empty);
    }

    public void DismissTrackNameEditOnClick(DependencyObject? clickSource)
    {
        var editingName = _editingTrackName;
        var editingTrack = _editingTrackModel;
        if (editingName is null || editingTrack is null || IsInside(clickSource, editingName)) return;

        // Defer the commit until the click has reached its target; committing a rename rebuilds
        // the controls and could otherwise steal the click from that target.
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (ReferenceEquals(_editingTrackName, editingName) && !editingName.IsReadOnly)
                FinishTrackNameEdit(editingName, editingTrack, commit: true);
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private int TrackIndexAtY(double y)
    {
        var count = _project?.Tracks.Count ?? 0;
        if (count == 0) return -1;
        var index = RowIndexAt(_project, Math.Max(0, y));
        return index < 0 ? count - 1 : index;
    }

    private void UpdateDragVisual(Point? pointer = null)
    {
        var accent = (Brush)Application.Current.FindResource("AccentBrush");
        var accentSoft = (Brush)Application.Current.FindResource("AccentSoftBrush");
        var soft = (Brush)Application.Current.FindResource("BorderSoftBrush");
        var count = _trackRows.Count;
        var from = _dragFromTrack;
        var to = _dragTargetTrack;
        var floating = _dragArmed && from >= 0 && from < count;
        var shifting = floating && to >= 0 && to != from;

        // Mirror the drag on the timeline so the whole lane (including the MIDI area) highlights,
        // not just the left-hand track controls.
        _timeline.SetDragPreview(
            floating ? from : -1,
            floating ? to : -1,
            floating && pointer.HasValue ? pointer.Value.Y - _dragOrigin.Y : 0);
        LayoutDragLaneOutline();

        for (var i = 0; i < count; i++)
        {
            var row = _trackRows[i];
            var shift = _rowTransforms[i];
            // While rows slide they are GPU textures: moving one costs a texture blit, not a re-rasterise
            // of its buttons, combo box and text on every display refresh.
            var rowCache = floating ? _rowDragCache ??= new BitmapCache(VisualTreeHelper.GetDpi(this).PixelsPerDip) { EnableClearType = true, SnapsToDevicePixels = true } : null;
            if (!ReferenceEquals(row.CacheMode, rowCache)) row.CacheMode = rowCache;

            if (floating && i == from)
            {
                // Picked-up row: follows the pointer and reads as "lifted" (browser-tab style).
                var fromTop = RowTopOf(_project, from);
                var y = pointer?.Y ?? fromTop + TrackRowHeight / 2.0;
                shift.BeginAnimation(TranslateTransform.YProperty, null);
                shift.Y = y - (fromTop + TrackRowHeight / 2.0);
                Panel.SetZIndex(row, 1000);
                row.Opacity = 1.0;
                row.Background = accentSoft;
                row.BorderBrush = accent;
                row.BorderThickness = new Thickness(1);
                row.Effect = _dragShadow ??= new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 8, ShadowDepth = 3, Direction = 270, Opacity = 0.6, Color = Colors.Black,
                    // Cheaper blur kernel (the shadow is re-blurred every frame while the row moves).
                    RenderingBias = System.Windows.Media.Effects.RenderingBias.Performance
                };
                continue;
            }

            // Neighbouring rows slide up/down to open a gap at the drop position.
            double gap = 0;
            if (shifting)
            {
                var moving = RowHeightOf(_project, _project?.Tracks.ElementAtOrDefault(from));
                if (from < to && i > from && i <= to) gap = -moving;
                else if (from > to && i >= to && i < from) gap = moving;
            }
            if (Math.Abs((double)shift.GetAnimationBaseValue(TranslateTransform.YProperty) - gap) > 0.1)
            {
                shift.Y = gap;
                var duration = UiMotion.DurationMilliseconds(105);
                if (duration <= 0)
                {
                    shift.BeginAnimation(TranslateTransform.YProperty, null);
                    shift.Y = gap;
                }
                else shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(gap, TimeSpan.FromMilliseconds(duration))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                }, HandoffBehavior.SnapshotAndReplace);
            }
            Panel.SetZIndex(row, 0);
            row.Opacity = 1.0;
            row.Effect = null;
            row.BorderBrush = soft;
            row.BorderThickness = new Thickness(0, 0, 1, 1);
            row.Background = i == _selectedTrackIndex ? accentSoft : TintBrush(i);
        }
    }
}
