using System;
using System.Diagnostics;
using System.IO;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Resizing the Settings window must only lay out: a resize step is saved quietly (no SettingsChanged broadcast that makes every consumer
/// re-apply the settings), never rebuilds the rows, and stays under a per-step budget.
/// Tests: this class.
/// </summary>
public class ResizeLagBenchTests
{
    /// <summary>Measured about 60-110 ms per step in a debug headless run on a busy machine (the broadcast made it 100-600 ms); the budget leaves margin.</summary>
    private const double StepBudgetMs = 400;

    [AvaloniaFact]
    public void ResizingPreferencesBroadcastsNothingAndStaysInBudget()
    {
        var path = Path.Combine(Path.GetTempPath(), "tf-resize-" + Guid.NewGuid().ToString("N") + ".json");
        var settings = MainWindowSettings.Open(path);
        var notices = 0;
        settings.SettingsChanged += _ => notices++;
        var window = settings.CreatePreferencesWindow();
        window.Show();
        window.UpdateLayout();
        var rows = window.ShownRows.Count;
        var total = 0.0;
        const int steps = 20;
        for (var i = 0; i < steps; i++)
        {
            var watch = Stopwatch.StartNew();
            window.Width = 860 + i * 9;
            window.Height = 620 + i * 7;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            total += watch.Elapsed.TotalMilliseconds;
        }
        Assert.Equal(0, notices);
        Assert.Equal(rows, window.ShownRows.Count);
        Assert.True(total / steps < StepBudgetMs, $"{total / steps:F0} ms per resize step");
        window.Close();
    }
}
