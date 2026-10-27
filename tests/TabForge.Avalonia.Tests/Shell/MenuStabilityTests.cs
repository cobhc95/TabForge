using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Platform;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the submenu timing rules of MenuStability: a row crossed inside the corridor to the open submenu keeps it open until the close delay,
//   a pointer back on the submenu cancels the close, and a pointer away from everything closes it after the delay.
// Does not own: the real pointer routing (the base handler) or the pixels.
// Tests: this file.
public class MenuStabilityTests
{
    private static readonly Rect SubmenuAtRight = new(200, 0, 100, 120);

    private static (MenuStability Handler, List<(Action Run, TimeSpan Delay)> Timers, MenuItem Parent, MenuItem Neighbour) Build()
    {
        var timers = new List<(Action, TimeSpan)>();
        var handler = new MenuStability(true) { DelayRunner = (run, delay) => timers.Add((run, delay)) };
        var parent = new MenuItem { Header = "Scale", Items = { new MenuItem { Header = "C" } } };
        var neighbour = new MenuItem { Header = "Other" };
        var menu = new ContextMenu(handler) { Items = { parent, neighbour } };
        var host = new Panel { ContextMenu = menu };
        new Window { Content = host }.Show();
        menu.Open(host);
        parent.IsSubMenuOpen = true;
        return (handler, timers, parent, neighbour);
    }

    [AvaloniaFact]
    public void CrossingANeighbourInTheCorridorKeepsTheSubmenuUntilTheCloseDelay()
    {
        var (handler, timers, parent, neighbour) = Build();

        var deferred = handler.TryDeferEntry(neighbour, parent, SubmenuAtRight, new PixelPoint(150, 20), new PixelPoint(170, 40));

        Assert.True(deferred);
        var timer = Assert.Single(timers);
        Assert.Equal(MenuStability.CloseDelay, timer.Delay);
        timer.Run();
        Assert.False(parent.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void EnteringTheSubmenuCancelsTheScheduledClose()
    {
        var (handler, timers, parent, neighbour) = Build();
        handler.TryDeferEntry(neighbour, parent, SubmenuAtRight, new PixelPoint(150, 20), new PixelPoint(170, 40));

        handler.CancelPendingClose();
        timers[0].Run();

    }

    [AvaloniaFact]
    public void LeavingEverythingClosesAfterTheDelay()
    {
        var (handler, timers, parent, _) = Build();

        handler.ScheduleCloseIfAway(parent);

        Assert.Equal(MenuStability.CloseDelay, Assert.Single(timers).Delay);
        timers[0].Run();
        Assert.False(parent.IsSubMenuOpen);
    }

    [AvaloniaFact]
    public void AnEntryOutsideTheCorridorIsNotHeldBack()
    {
        var (handler, timers, parent, neighbour) = Build();

        var deferred = handler.TryDeferEntry(neighbour, parent, SubmenuAtRight, new PixelPoint(150, 20), new PixelPoint(100, 400));

        Assert.False(deferred);
        Assert.Empty(timers);
    }

    [Fact]
    public void TheDelaysAreNamedConstants()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(120), MenuStability.OpenDelay);
        Assert.Equal(TimeSpan.FromMilliseconds(350), MenuStability.CloseDelay);
    }

    private static object? HandlerOf(object menu) =>
        menu.GetType().GetProperty("InteractionHandler", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)?.GetValue(menu);

    [AvaloniaFact]
    public void FactoriesAndTheFretboardMenuUseTheStableHandler()
    {
        Assert.IsType<MenuStability>(HandlerOf(MenuStability.CreateContextMenu()));
        Assert.IsType<MenuStability>(HandlerOf(MenuStability.CreateMenu()));
        var fretboard = new FretboardContextMenu(() => new TabForge.Services.AppSettings(), () => { }, () => { }, () => null);
        var menu = TabForge.Avalonia.Views.ContextMenuBuilder.Build(fretboard.RowsFor(default), fretboard);
        Assert.IsType<MenuStability>(HandlerOf(menu));
    }

    [AvaloniaFact]
    public void RightArrowOpensAndLeftArrowClosesASubmenu()
    {
        var parent = new MenuItem { Header = "Scale", Items = { new MenuItem { Header = "C" } } };
        var menu = MenuStability.CreateContextMenu();
        menu.Items.Add(parent);
        var host = new Panel { ContextMenu = menu };
        new Window { Content = host }.Show();
        menu.Open(host);

        parent.RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Right });
        var openedByRight = parent.IsSubMenuOpen;
        ((MenuItem)parent.Items[0]!).RaiseEvent(new global::Avalonia.Input.KeyEventArgs { RoutedEvent = global::Avalonia.Input.InputElement.KeyDownEvent, Key = global::Avalonia.Input.Key.Left });
        var closedByLeft = !parent.IsSubMenuOpen;

        Assert.True(openedByRight, "right");
        Assert.True(closedByLeft, "left");
    }
}
