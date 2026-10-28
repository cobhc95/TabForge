using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Audio.Contracts.Builtin;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the built-in effect editors fit the area they are in: at 500, 800 and 1200 DIP wide no visible control reaches past the right edge of the host, the EQ bands wrap 4, 2, 1 per row,
//   and each editor is rendered at 500 and 1200 for review.
// Does not own: the editors' behaviour (BuiltinEditorWindowTests).
// Tests: this class. Renders go to TABFORGE_SHOTS_DIR when it is set, else the render folder of this class.
public sealed class BuiltinEditorResponsiveTests
{
    private static (Window Window, BuiltinEditorHost Host) Open(string id, double width, double height = 700)
    {
        var info = BuiltinEffects.Find(id)!;
        var slot = new PluginSlot { Name = info.Name, Path = id, Format = PluginFormats.Builtin, Vendor = BuiltinEffects.Vendor };
        var track = FxTestKit.Track([slot], "Guitar");
        var host = new BuiltinEditorHost();
        var window = new Window { Width = width, Height = height, Content = host };
        window.Show();
        Assert.True(host.Show(FxTestKit.Host(track, new FxTestKit.FakeEngine()), track, slot));
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        return (window, host);
    }

    [AvaloniaTheory]
    [InlineData(BuiltinEffects.EqId)]
    [InlineData(BuiltinEffects.CompressorId)]
    [InlineData(BuiltinEffects.LimiterId)]
    [InlineData(BuiltinEffects.DelayId)]
    [InlineData(BuiltinEffects.GateId)]
    [InlineData(BuiltinEffects.IrLoaderId)]
    public void NoControlReachesPastTheHostAtAnyWidth(string id)
    {
        foreach (var width in new double[] { 500, 800, 1200 })
        {
            var (window, host) = Open(id, width);
            foreach (var control in host.GetVisualDescendants().OfType<Control>().Where(c => c.IsEffectivelyVisible && c.Bounds.Width > 0))
            {
                var right = control.TranslatePoint(new Point(control.Bounds.Width, 0), host)!.Value.X;
                Assert.True(right <= host.Bounds.Width + 0.5, $"{id} @{width}: {control.GetType().Name} {control.Name} right {right} > {host.Bounds.Width}");
            }
            if (Environment.GetEnvironmentVariable("TABFORGE_SHOTS_DIR") is { Length: > 0 } dir && width != 800)
            {
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(Directory.CreateDirectory(dir).FullName, $"{id.Replace('/', '-').Replace(':', '-')}-{width}.png"));
            }
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TheEqBandsWrapFourTwoOne()
    {
        int Columns(double width)
        {
            var (window, host) = Open(BuiltinEffects.EqId, width);
            var xs = host.GetVisualDescendants().OfType<ResponsiveColumns>().First().Children.Select(c => Math.Round(c.Bounds.X)).Distinct().Count();
            window.Close();
            return xs;
        }
        Assert.Equal(4, Columns(1200));
        Assert.Equal(2, Columns(500));
        Assert.Equal(1, Columns(300));
    }
}
