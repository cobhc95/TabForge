using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using TabForge.Avalonia.Platform.Controls;

namespace TabForge.Avalonia.Views;

// Owns: the lane's show/hide chevron, at the right end of the lane header strip: a flat header button (FlatToolButton) whose glyph points down while the
//   lane shows and up while it is hidden. A press flips the lane (the same as a press on the strip outside the lane picker).
// Does not own: the picker (PianoRollView.LaneButton), the strip's paint and its own header press (PianoRollSurface).
// Tests: TabForge.Avalonia.Tests.PianoRollLaneRealClickTests.
public sealed partial class PianoRollView
{
    /// <summary>The chevron's tooltip and automation name.</summary>
    internal const string LaneToggleTip = "Show or hide the lane";

    private const string LaneShownGlyph = "▾", LaneHiddenGlyph = "▴";

    private readonly Button _laneToggle = FlatToolButton.Create(new TextBlock { Text = LaneShownGlyph, VerticalAlignment = VerticalAlignment.Center });

    /// <summary>Builds the chevron (the strip's right end), kept on the strip by <see cref="PlaceLaneToggle"/>.</summary>
    internal Control BuildLaneToggle()
    {
        _laneToggle.Name = "PianoRollLaneToggle";
        _laneToggle.Height = PianoRollSurface.LaneHeaderSize;
        _laneToggle.MinWidth = PianoRollToolbarLayout.ToolMinWidth;
        _laneToggle.HorizontalAlignment = HorizontalAlignment.Right;
        _laneToggle.VerticalAlignment = VerticalAlignment.Bottom;
        ToolTip.SetTip(_laneToggle, LaneToggleTip);
        AutomationProperties.SetName(_laneToggle, LaneToggleTip);
        _laneToggle.Click += (_, _) =>
        {
            Surface.LaneShown = !Surface.LaneShown;
            Surface.LaneChanged?.Invoke();   // SaveLane: the settings, the picker and the chevron follow
            Surface.InvalidateVisual();
        };
        PlaceLaneToggle();
        return _laneToggle;
    }

    /// <summary>Keeps the chevron on the strip with the picker and points it at the lane's state.</summary>
    private void PlaceLaneToggle()
    {
        _laneToggle.SetValue(Layoutable.MarginProperty, UiMetrics.Sides(None, None, None, Surface.LaneShown ? Surface.Lane : None, 1));
        if (_laneToggle.Content is TextBlock glyph) glyph.Text = Surface.LaneShown ? LaneShownGlyph : LaneHiddenGlyph;
    }
}
