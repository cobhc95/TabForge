using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a scaled window's content filling the whole window when the window is resized (UiScaleRoot), shown on the MIDI processing window.
/// Does not own: the window's own layout (MidiSmallLayoutTests).
/// Tests: this class.
/// </summary>
public class UiScaleRootTests
{
    [AvaloniaTheory]
    [InlineData(0.75, 1150, 760)] [InlineData(0.75, 1600, 900)] [InlineData(1.0, 1150, 760)] [InlineData(1.25, 1300, 800)] [InlineData(1.5, 1600, 1000)]
    public void ScaledContentFillsAResizedWindow(double scale, double width, double height)
    {
        WindowUiScale.CurrentAppearance.UiScale = scale;
        try
        {
            var info = MidiProcessorCatalog.Find("velocity")!;
            var slot = new PluginSlot { Name = "P", Type = PluginSlotType.Instrument, MidiProcessors = { MidiProcessorCatalog.Create(info) } };
            var track = FxTestKit.Track(new[] { slot });
            var window = new MidiProcessingWindow(FxTestKit.Host(track, new FxTestKit.FakeEngine()), track, slot, a => a());
            window.Show();
            window.Width = width; window.Height = height;
            for (var i = 0; i < 8; i++) { DialogTestKit.RunQueuedUiWork(); window.UpdateLayout(); }
            var root = ((Visual)window).GetVisualDescendants().OfType<LayoutTransformControl>().First();
            var content = (Control)root.Child!;
            // In the root's own (unscaled) units the content sits at its margin and reaches the window's far edges.
            var b = content.Bounds;
            Assert.InRange(b.X, content.Margin.Left - 1, content.Margin.Left + 1);
            Assert.InRange(b.Y, content.Margin.Top - 1, content.Margin.Top + 1);
            Assert.InRange(b.Right + content.Margin.Right, width / scale - 1, width / scale + 1);
            Assert.InRange(b.Bottom + content.Margin.Bottom, height / scale - 1, height / scale + 1);
            window.Close();
        }
        finally { WindowUiScale.CurrentAppearance.UiScale = 1.0; }
    }
}
