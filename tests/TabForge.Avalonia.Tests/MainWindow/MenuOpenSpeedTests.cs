using System.Diagnostics;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Views;
using Xunit;
using Xunit.Abstractions;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: how long each main menu and the FX window's menus take to open (the work their open handlers do before the popup can show), with a budget generous enough
/// for a loaded machine: a menu that does file or settings work when it opens shows here first.
/// Does not own: the menu contents (MainWindowMenuTests) or the popup painting, which headless does not do.
/// Tests: this class.
/// </summary>
public class MenuOpenSpeedTests
{
    // Per open, after one warm-up open. A menu opens on a click: past about 100 ms it feels late, so the budget is a loose ceiling that only a real regression crosses.
    private const double BudgetMs = 150;
    private readonly ITestOutputHelper _out;
    public MenuOpenSpeedTests(ITestOutputHelper output) => _out = output;

    private static double OpenOnce(MenuItem item)
    {
        var clock = Stopwatch.StartNew();
        item.IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        var ms = clock.Elapsed.TotalMilliseconds;
        item.IsSubMenuOpen = false;
        Dispatcher.UIThread.RunJobs();
        return ms;
    }

    [AvaloniaFact]
    public void EveryMainMenuOpensWithinTheBudget()
    {
        var main = new MainWindow { Width = 1320, Height = 900 };
        main.Show();
        Dispatcher.UIThread.RunJobs();
        var menu = main.GetVisualDescendants().OfType<Menu>().Single(m => m.Name == "MainMenu");
        var slow = new System.Collections.Generic.List<string>();
        foreach (var top in menu.Items.OfType<MenuItem>())
        {
            OpenOnce(top);   // warm-up: JIT and first template pass
            var runs = new[] { OpenOnce(top), OpenOnce(top), OpenOnce(top) };
            _out.WriteLine($"{top.Header,-12} {runs.Min():0.0} ms best, {runs.Max():0.0} ms worst, {top.Items.Count} items");
            if (runs.Min() > BudgetMs) slow.Add($"{top.Header}: {runs.Min():0.0} ms");
        }
        main.Close();
        Assert.True(slow.Count == 0, "Menus slower than " + BudgetMs + " ms to open: " + string.Join(", ", slow));
    }

    [AvaloniaFact]
    public void TheFxWindowMenusOpenWithinTheBudget()
    {
        using var scratch = FxTestKit.ScratchLibrary();
        var window = FxTestKit.Window();
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var slow = new System.Collections.Generic.List<string>();
        foreach (var top in window.GetLogicalDescendants().OfType<Menu>().SelectMany(m => m.Items.OfType<MenuItem>()))
        {
            OpenOnce(top);
            var best = new[] { OpenOnce(top), OpenOnce(top), OpenOnce(top) }.Min();
            _out.WriteLine($"FX {top.Header,-10} {best:0.0} ms best");
            if (best > BudgetMs) slow.Add($"{top.Header}: {best:0.0} ms");
        }
        window.Close();
        Assert.True(slow.Count == 0, "FX menus slower than " + BudgetMs + " ms to open: " + string.Join(", ", slow));
    }
}
