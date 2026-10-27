using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: submenu stability under real headless pointer input: a diagonal sweep from a parent row into its submenu popup keeps the submenu open.
// Does not own: the timing rules (MenuStabilityTests).
// Tests: this file.
public class MenuRealInputTests
{
    // Runs the dispatcher loop (jobs and timers) for the given time.
    private static void Pump(int ms)
    {
        using var cts = new System.Threading.CancellationTokenSource();
        System.Threading.Tasks.Task.Delay(ms).ContinueWith(_ => cts.Cancel());
        Dispatcher.UIThread.MainLoop(cts.Token);
    }

    private static Point CentreIn(Visual target, Visual root) =>
        target.TranslatePoint(new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), root)!.Value;

    [AvaloniaFact]
    public void PointerLeavingTheWholeMenuClosesTheSubmenuAfterTheDelay()
    {
        var parent = new MenuItem { Header = "Scale", Items = { new MenuItem { Header = "C" } } };
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(parent);
        var host = new Panel { Width = 400, Height = 300 };
        var window = new Window { Content = host, Width = 400, Height = 300 };
        window.Show();
        menu.Open(host);
        Pump(50);
        var root = (TopLevel)parent.GetVisualRoot()!;
        root.MouseMove(CentreIn(parent, root));
        Pump(300);
        Assert.True(parent.IsSubMenuOpen);

        root.MouseMove(new Point(380, 280));
        Pump((int)MenuStability.CloseDelay.TotalMilliseconds + 200);

        Assert.False(parent.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void SweepingFromTheParentRowIntoTheSubmenuKeepsItOpen()
    {
        var child = new MenuItem { Header = "C" };
        var parent = new MenuItem { Header = "Scale", Items = { child, new MenuItem { Header = "D" } } };
        var other = new MenuItem { Header = "Other" };
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(parent);
        menu.Items.Add(other);
        var host = new Panel { Width = 400, Height = 300 };
        var window = new Window { Content = host, Width = 400, Height = 300 };
        window.Show();
        menu.Open(host);
        Pump(50);

        var log = "";
        var closer = "";
        var clicked = 0;
        child.Click += (_, _) => clicked++;
        parent.PropertyChanged += (_, e) => { if (e.Property == MenuItem.IsSubMenuOpenProperty && e.NewValue is false) closer += string.Join("|", Environment.StackTrace.Split((char)10).Where(f => f.Contains("Menu") || f.Contains("Timer") || f.Contains("Dispatcher")).Select(f => f.Trim().Split(" in ")[0])); };
        var parentRoot = (TopLevel)parent.GetVisualRoot()!;
        log += $"parentRoot={parentRoot.GetType().Name} isWindow={ReferenceEquals(parentRoot, window)}; ";
        var start = CentreIn(parent, parentRoot);
        parentRoot.MouseMove(start);
        Pump(300);
        log += $"afterHover open={parent.IsSubMenuOpen} over={parent.IsPointerOver} start={start} bounds={parent.Bounds} sel={menu.SelectedItem}; ";
        Assert.True(parent.IsSubMenuOpen, log);

        var childRoot = child.GetVisualRoot() as TopLevel;
        log += $"childRoot={childRoot?.GetType().Name} same={ReferenceEquals(childRoot, parentRoot)}; ";
        Assert.NotNull(childRoot);
        var target = CentreIn(child, childRoot!);
        // The sweep: small steps from the parent row centre towards the child row, in the coordinates of whichever root holds the pointer.
        var sameRoot = ReferenceEquals(childRoot, parentRoot);
        var screenStart = parentRoot.PointToScreen(start);
        var screenEnd = childRoot!.PointToScreen(target);
        const int steps = 12;
        for (var i = 1; i <= steps; i++)
        {
            var screen = new PixelPoint(screenStart.X + (screenEnd.X - screenStart.X) * i / steps, screenStart.Y + (screenEnd.Y - screenStart.Y) * i / steps);
            var inChild = new Rect(childRoot.PointToScreen(new Point(0, 0)).X, childRoot.PointToScreen(new Point(0, 0)).Y, childRoot.Bounds.Width, childRoot.Bounds.Height).Contains(new Point(screen.X, screen.Y));
            var root = sameRoot || inChild ? childRoot : parentRoot;
            root.MouseMove(root.PointToClient(screen));
            Pump(20);
            log += $"[{i}:{screen.X},{screen.Y} in={(inChild ? "child" : "parent")} open={parent.IsSubMenuOpen}] ";
        }
        Pump(500);
        Assert.True(parent.IsSubMenuOpen, log + closer);

        childRoot.MouseDown(childRoot.PointToClient(screenEnd), MouseButton.Left);
        childRoot.MouseUp(childRoot.PointToClient(screenEnd), MouseButton.Left);
        Pump(50);
        Assert.Equal(1, clicked);
    }
}
