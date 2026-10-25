using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Markup.Xaml.MarkupExtensions;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Styling;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Shell;

namespace TabForge.Avalonia.Views;

// Owns: dragged-tab, drop-preview and slide helpers.
// Does not own: the rest of this type (see the main file).
// Tests: no dedicated test class.
public sealed partial class TabStripView : UserControl
{
    /// <summary>The horizontal offset of the tab being dragged, in DIPs from its laid-out place; 0 when no tab is dragged.</summary>
    public double DraggedTabOffset => _draggedOffset;

    /// <summary>Makes the tab at <paramref name="index"/> take <paramref name="offset"/> from its laid-out place at the next Show, then ease
    /// it to its place: a drop that reorders the tabs carries the released tab's position over to its new slot, so it does not jump.</summary>
    public void CarryDraggedTab(int index, double offset) => _carry = (index, offset);

    /// <summary>Ends the visuals of a tab drag: the tab eases back to its laid-out place and loses its lift, the press and its capture are
    /// released, and the drop preview is hidden. Called when a drag ends, by release, Escape or a lost capture.</summary>
    public void EndDrag()
    {
        if (_press is { } held)
        {
            _press = null;
            held.Pointer.Capture(null);
        }
        _draggedTab = null;
        _draggedOffset = 0;
        _motion.ResetAll();
        HideDropPreview();
    }

    private void BeginDrag(int index, Border tab)
    {
        _draggedTab = index;
        _draggedOffset = 0;
        TabStripMotion.Lift(tab);
    }

    // The dragged tab's left edge follows the pointer 1:1: the grabbed point stays under it, and the tab stays inside the strip.
    private void FollowPointer(Control tab, Point pointerInStrip, Point grabInTab)
    {
        var origin = _tabs.TranslatePoint(default, this) ?? default;
        var slotLeft = origin.X + tab.Bounds.X;
        var lowest = origin.X;
        var highest = Math.Max(lowest, origin.X + _tabs.Bounds.Width - tab.Bounds.Width);
        var left = Math.Clamp(pointerInStrip.X - grabInTab.X, lowest, highest);
        _draggedOffset = left - slotLeft;
        _motion.Follow(tab, _draggedOffset);
    }

    /// <summary>The point of the pressed tab the pointer grabbed, in that tab's own coordinates, while a tab drag runs; null otherwise.</summary>
    public Point? DragGrabInTab => _press is { Dragging: true } held ? held.InTab : null;

    /// <summary>Hides the tab at <paramref name="index"/> (null shows every tab) while its drag tears it off; kept across Show.</summary>
    public void SetHiddenTab(int? index)
    {
        _hiddenTab = index;
        for (var i = 0; i < _tabs.Children.Count; i++) _tabs.Children[i].IsVisible = i != index;
    }

    /// <summary>Shows the drop preview: the insertion marker before the tab at <paramref name="insertionIndex"/> (after the last tab when it
    /// equals the tab count) and, when <paramref name="highlightStrip"/> is set, the accent highlight around the strip.</summary>
    public void ShowDropPreview(int insertionIndex, bool highlightStrip)
    {
        _insertMarker.Margin = new Thickness(InsertionMarkerLeft(insertionIndex), 0, 0, 0);
        _insertMarker.Height = Settings.TabHeight;
        _insertMarker.IsVisible = true;
        _dropHighlight.IsVisible = highlightStrip;
    }

    public void HideDropPreview()
    {
        _insertMarker.IsVisible = false;
        _dropHighlight.IsVisible = false;
    }

    /// <summary>True while the accent highlight around the strip shows.</summary>
    public bool IsDropHighlightShown => _dropHighlight.IsVisible;

    /// <summary>True while the insertion marker shows.</summary>
    public bool IsInsertionMarkerShown => _insertMarker.IsVisible;

    /// <summary>Moves each neighbouring tab by its offset (DIPs) to preview a reorder; the others ease over <see cref="SlideMilliseconds"/>
    /// when <paramref name="animate"/> is set. The dragged tab is left to the pointer. UI thread only.</summary>
    private void SlideTabs(IReadOnlyList<double> offsets, bool animate)
    {
        _motion.Slide(offsets, animate, _draggedTab);
    }

    /// <summary>Puts every neighbouring tab back on its laid-out place at once; the dragged tab stays with the pointer.</summary>
    public void ClearSlides() => SlideTabs(new double[_tabs.Children.Count], animate: false);

    private double InsertionMarkerLeft(int insertionIndex)
    {
        if (_tabs.Children.Count == 0) return 0;
        var before = insertionIndex < _tabs.Children.Count;
        var tab = _tabs.Children[before ? insertionIndex : _tabs.Children.Count - 1];
        var edge = before ? new Point(0, 0) : new Point(tab.Bounds.Width, 0);
        var x = tab.TranslatePoint(edge, this)?.X ?? 0;
        // The marker sits in the gap: half a gap left of a tab's left edge, half a gap right of the last tab's right edge.
        return x + (before ? -TabSpacing : TabSpacing) / 2 - _insertMarker.Width / 2;
    }

    private void RunMiddleClick(int index)
    {
        switch (TabBehaviourSettings.MiddleClickAction(Settings))
        {
            case TabMiddleClickAction.Close: CloseRequested?.Invoke(index); break;
            case TabMiddleClickAction.NewTab: NewTabRequested?.Invoke(); break;
            case TabMiddleClickAction.Duplicate: DuplicateRequested?.Invoke(index); break;
        }
    }
}
