using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>Owns: the title bar's Monitor FX button follows the monitor chain's on/off state and menu choices. Tests: Shell/MonitorFxCaptionButton, MonitorFxBinding.</summary>
public class MonitorFxTitleButtonTests
{
    [AvaloniaFact]
    public void ButtonStateFollowsMonitorChain()
    {
        var state = (On: false, Plugins: 2, Global: true);
        var opened = 0;
        var binding = new MonitorFxBinding(() => state, () => opened++, on => state.On = on, global => state.Global = global);
        var bar = new TitleBarView(new TabStripView(new TabForge.Services.TabSettings()), new NullChrome(), () => { }, () => { }, monitor: binding);
        var window = new Window { Width = 900, Height = 300, Content = bar };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var button = bar.GetVisualDescendantsOf<Button>().Single(b => b.Name == "MonitorFx");
        var glyph = button.GetVisualDescendantsOf<global::Avalonia.Controls.Shapes.Path>().Single();
        var grey = glyph.Stroke;
        Assert.Contains("off", ToolTip.GetTip(button)?.ToString());

        state.On = true;
        binding.RaiseChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.NotEqual(grey, glyph.Stroke);
        Assert.Contains("Program-wide · 2 plug-ins · on", ToolTip.GetTip(button)?.ToString());

        state = (true, 0, false);
        binding.RaiseChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(grey, glyph.Stroke);
        Assert.Contains("This song", ToolTip.GetTip(button)?.ToString());

        DialogTestKit.Click(button);
        Assert.Equal(1, opened);
    }

    private sealed class NullChrome : TabForge.Platform.IWindowChrome
    {
        public TabForge.Platform.WindowChromeState State => TabForge.Platform.WindowChromeState.Normal;
        public event System.EventHandler? StateChanged { add { } remove { } }
        public void BeginMove() { }
        public void BeginResize(TabForge.Platform.ResizeEdge edge) { }
        public void Minimize() { }
        public void ToggleMaximise() { }
        public void Close() { }
    }
}
