using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Presets;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the timeline zoom pair's place in the transport row: the last group, after the master volume knob, and the row stays one line at the
/// default track list width.
/// Does not own: the zoom commands (MainWindowTimelineZoom), the one-row size fit rules (TransportWrapRules) or the knob itself.
/// Tests: TabForge.Avalonia.Tests.TransportZoomPairOrderTests.
/// </summary>
public class TransportZoomPairOrderTests
{
    [AvaloniaFact]
    public void ZoomPairClosesTheTransportRowAfterTheMasterKnobAndTheRowStaysOneLine()
    {
        var document = new ScorePreviewEditor.PreviewDocument(DemoSongFactory.Create());
        var view = new ArrangementView(document.Project, document);
        var window = new Window { Width = 1300, Height = 420, Content = view };
        window.Show();
        window.UpdateLayout();

        var bar = view.Transport;
        var row = (WrapPanel)bar.Child!;
        var zoomGroup = (StackPanel)row.Children[^1];
        var knobGroup = (StackPanel)row.Children[^2];
        Assert.Contains(bar.MasterVolumeKnob, knobGroup.Children);
        Assert.Equal(new[] { "Zoom in timeline", "Zoom out timeline" }, zoomGroup.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Select(button => button.Item.Name));
        // One line means the wrapped row is no taller than its tallest group (a button's 34 DIP plus its 1 DIP margins on each side).
        var tallestGroup = row.Children.Max(group => group.Bounds.Height);
        Assert.True(TransportWrapRules.FitsOneRow(row.Bounds.Height, tallestGroup), $"the transport row is {row.Bounds.Height} tall, its tallest group {tallestGroup}");
    }
}
