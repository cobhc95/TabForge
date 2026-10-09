using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>What the section-lane hint needs from the timeline (implemented by <see cref="TrackTimeline"/>).</summary>
internal interface ISectionTipHost
{
    SongProject? Project { get; }
    bool IsMouseOver { get; }
    /// <summary>A bar, marker or section drag is in progress (no hint then).</summary>
    bool SectionGestureActive { get; }
    /// <summary>The section under the pointer (-1 = none).</summary>
    int HoverSectionIndex { get; }
}

// Owns: the section lane's hover hint (its ToolTip, the open delay timer, and the hint text for a section).
// Does not own: which section is hovered (TrackTimeline's hover), section dragging, the section model.
// Tests: TestTimelineHoverAndBarMarker.
/// <summary>
/// The section lane shows its hint through its own ToolTip, opened after a short hover: a tooltip
/// assigned while the pointer is already inside the timeline never opens on its own in WPF.
/// </summary>
internal sealed class SectionTipController
{
    private readonly ISectionTipHost _host;
    private readonly UIElement _placementTarget;
    private ToolTip? _tip;
    private DispatcherTimer? _timer;

    public SectionTipController(ISectionTipHost host, UIElement placementTarget)
    {
        _host = host;
        _placementTarget = placementTarget;
    }

    /// <summary>What dragging this particular section will do (Ctrl+drag, the marker-only move, needs free bars beside it).</summary>
    internal string TextFor(int sectionIndex)
    {
        var project = _host.Project;
        var sorted = project is null ? new List<MarkerModel>() : SectionLayout.Sorted(project);
        if (sectionIndex < 0 || sectionIndex >= sorted.Count) return "";
        var marker = sorted[sectionIndex];
        var title = string.IsNullOrWhiteSpace(marker.Title) ? "Section" : marker.Title;
        var addHint = TooltipShortcuts.Append("Add section", "Section.Add");   // the key follows rebinding
        if (marker.LockPosition) return $"{title} (position locked)\nRight-click: section options · {addHint}";
        // Plain drag moves the section with its bars; Ctrl+drag moves only the marker (needs free bars beside it).
        var drag = $"Drag: move {title} with its bars (other sections make room)";
        string ctrl;
        if (SectionLayout.MoveRange(project!, marker) is var (min, max))
        {
            var left = min < marker.MeasureIndex; var right = max > marker.MeasureIndex;
            var where = left && right ? "left or right" : left ? "left" : "right";
            ctrl = $"Ctrl+drag: move only the {title} marker {where} into the free bars (its bars stay, a gap is left behind)";
        }
        else ctrl = $"Ctrl+drag: can't move the marker alone (no empty bars beside {title})";
        return $"{drag}\n{ctrl}\n" +
               $"Drag an edge: resize · Right-click: section options · {addHint}";
    }

    internal void Schedule(int sectionIndex)
    {
        _timer?.Stop();
        if (_tip is not null) _tip.IsOpen = false;
        if (sectionIndex < 0 || _host.SectionGestureActive) return;
        _timer ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _timer.Tick -= Timer_Tick;
        _timer.Tick += Timer_Tick;
        _timer.Start();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        _timer?.Stop();
        if (_host.HoverSectionIndex < 0 || !_host.IsMouseOver || _host.SectionGestureActive) return;
        _tip ??= new ToolTip { Placement = PlacementMode.Mouse, PlacementTarget = _placementTarget };
        _tip.Content = TextFor(_host.HoverSectionIndex);
        _tip.IsOpen = true;
    }

    internal void Close()
    {
        _timer?.Stop();
        if (_tip is not null) _tip.IsOpen = false;
    }
}
