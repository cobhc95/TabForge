using System.Windows;
using TabForge.Services;

namespace TabForge.Diagnostics;

// Window probes, instrument panel menu probe.
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    /// <summary>`--probe-instrument-menu &lt;report&gt;`: the instrument panel's right-click menu in each view.</summary>
    public void RunInstrumentMenuProbe(string reportPath)
    {
        var path = FilePathPolicy.OutputFile(reportPath, "instrument menu report");
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(async () =>
        {
            var report = new System.Text.StringBuilder();
            try
            {
                await Task.Delay(1500);
                foreach (var view in new[] { InstrumentViews.Fretboard, InstrumentViews.Keyboard, InstrumentViews.Drums })
                {
                    SetInstrumentView(view);
                    await Task.Delay(300);
                    Instrument.ContextMenu = null;
                    var args = new System.Windows.Input.MouseButtonEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0, System.Windows.Input.MouseButton.Right)
                        { RoutedEvent = UIElement.MouseRightButtonUpEvent, Source = Instrument };
                    Instrument.RaiseEvent(args);
                    await Task.Delay(200);
                    report.AppendLine($"== {view}: shows keyboard={Instrument.ShowsKeyboard}");
                    void Walk(System.Windows.Controls.ItemsControl items, string indent)
                    {
                        foreach (var item in items.Items.OfType<System.Windows.Controls.MenuItem>())
                        {
                            report.AppendLine($"{indent}{item.Header}{(item.IsChecked ? " [x]" : "")}");
                            if (indent.Length < 4) Walk(item, indent + "  ");
                        }
                    }
                    if (Instrument.ContextMenu is { } menu) { Walk(menu, ""); menu.IsOpen = false; }
                    else report.AppendLine("  (no menu)");
                }
            }
            catch (Exception ex) { report.AppendLine($"probe failed: {ex}"); }
            DiagnosticFileService.WriteText(path, report.ToString());
            _confirmOnClose = false;
            Application.Current.Shutdown(0);
        }));
    }
}
