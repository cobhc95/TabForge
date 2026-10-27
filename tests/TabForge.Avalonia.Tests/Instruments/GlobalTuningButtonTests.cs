using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: a click on the transport bar's Global tuning button opens the Global tuning window over the main window, as the Tools menu's command does
/// (the window's choice is applied by MainWindowGlobalTuning, which the menu and the button share).
/// Does not own: the tuning window's controls (GlobalTuningWindow), the retune rule (GlobalRetune), or the button's double-click and right-click
/// popups (TransportBarView).
/// Tests: TabForge.Avalonia.Tests.GlobalTuningButtonTests.
/// </summary>
public sealed class GlobalTuningButtonTests
{
    private const string TuningButtonName = "Global tuning";

    [AvaloniaFact]
    public void ClickingTheTuningButtonOpensTheGlobalTuningWindow()
    {
        var main = new MainWindow { Width = 2000, Height = 1100 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        main.UpdateLayout();

        var button = main.GetVisualDescendants().OfType<TransportBarView.TransportButton>().Single(b => b.Item.Name == TuningButtonName);
        button.Pressed?.Invoke();
        Dispatcher.UIThread.RunJobs();

        var tuning = Assert.Single(main.OwnedWindows.OfType<GlobalTuningWindow>());
        tuning.Close();
        main.Close();
    }
}
