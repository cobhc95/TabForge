using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Platform;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace TabForge.Avalonia.Platform;

// Owns: the pointer timing of menus and submenus so they behave like the usual ones: a submenu parent opens after OpenDelay, an open
//   submenu closes only CloseDelay after the pointer left it (cancelled when the pointer enters the submenu or comes back), and a row of
//   the parent menu crossed on the way to the open submenu (the pointer is inside the triangle from its last position to the submenu's
//   near edge) does not switch the submenu.
// Does not own: the menu rows, the templates (AvaloniaMenuTemplates, which also overlaps a submenu popup with its parent so no gap is
//   crossed), or click and keyboard opening (the base handler keeps a click-opened submenu open until Esc, Left or a click outside).
// Tests: TabForge.Avalonia.Tests.MenuStabilityTests.
public sealed class MenuStability : DefaultMenuInteractionHandler
{
    /// <summary>Time the pointer rests on a submenu parent row before its submenu opens.</summary>
    public static readonly TimeSpan OpenDelay = TimeSpan.FromMilliseconds(UiTiming.MenuOpenDelayMs);
    /// <summary>Time an open submenu survives the pointer leaving it for another row or for empty space.</summary>
    public static readonly TimeSpan CloseDelay = TimeSpan.FromMilliseconds(UiTiming.MenuCloseDelayMs);

    // Last pointer position on screen while over a menu; written and read on the UI thread only.
    private PixelPoint _lastPointer;
    // Bumped on every cancel or new schedule so a timer that fires late knows it is stale; UI thread only.
    private int _closeTicket;

    /// <summary>A handler for a context menu or a menu bar.</summary>
    public MenuStability(bool isContextMenu) : base(isContextMenu) => MenuShowDelay = OpenDelay;

    /// <summary>The only way the app makes a right-click menu, so every one has the stable submenu timing.</summary>
    public static ContextMenu CreateContextMenu() => new ScaledContextMenu(new MenuStability(true));

    /// <summary>The only way the app makes a menu bar.</summary>
    public static Menu CreateMenu() => new(new MenuStability(false));

    // ContextMenu owns a private popup (Avalonia 11.3.20 ContextMenu.Open) and makes itself that popup's child before it sets
    // IsOpen, so the menu's logical parent is the popup by the time IsOpen turns true. InheritsTransform is read only when the
    // popup sizes its host, before it opens, so it must be set here rather than on Opened; setting it again on a reused menu is a no-op.
    private sealed class ScaledContextMenu : ContextMenu
    {
        public ScaledContextMenu(IMenuInteractionHandler interactionHandler) : base(interactionHandler) { }

        // The subclass must look up the same theme/template as ContextMenu (styles keyed by its exact type), or it loses its look.
        protected override Type StyleKeyOverride => typeof(ContextMenu);

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == IsOpenProperty && change.GetNewValue<bool>() && Parent is Popup popup)
                popup.InheritsTransform = true;
        }
    }

    /// <summary>Runs an action after a delay; the dispatcher timer, replaced in tests.</summary>
    internal Action<Action, TimeSpan> DelayRunner { get; set; } = DispatcherRunOnce;

    private static void DispatcherRunOnce(Action action, TimeSpan delay) => DispatcherTimer.RunOnce(action, delay);

    protected override void PointerMoved(object? sender, PointerEventArgs e)
    {
        if (e.Source is Visual visual) _lastPointer = visual.PointToScreen(e.GetPosition(visual));
        base.PointerMoved(sender, e);
    }

    protected override void PointerEntered(object? sender, RoutedEventArgs e)
    {
        if (e.Source is Visual visual && e is PointerEventArgs pointer && ItemOf(e) is { } entered)
        {
            var now = visual.PointToScreen(pointer.GetPosition(visual));
            var open = OpenSiblingOf(entered);
            if (open is not null && TryDeferEntry(entered, open, SubmenuScreenRect(open), _lastPointer, now)) return;
            CancelPendingClose();
            _lastPointer = now;
        }
        base.PointerEntered(sender, e);
    }

    protected override void PointerExited(object? sender, RoutedEventArgs e)
    {
        if (ItemOf(e) is { IsTopLevel: false } exited && OpenRootOf(exited) is { } open)
        {
            ScheduleCloseIfAway(open);
            return;
        }
        base.PointerExited(sender, e);
    }

    // The base handler listens on the menu, so the sender is the menu and the row is the nearest MenuItem above the event's source.
    private static MenuItem? ItemOf(RoutedEventArgs e) => (e.Source as Visual)?.FindAncestorOfType<MenuItem>(includeSelf: true);

    /// <summary>True when the entry of <paramref name="entered"/> is held back: the pointer is inside the corridor towards the open
    /// submenu of <paramref name="open"/>, so a close check is scheduled instead of switching the submenu.</summary>
    internal bool TryDeferEntry(MenuItem entered, MenuItem open, Rect submenuScreen, PixelPoint last, PixelPoint now)
    {
        if (ReferenceEquals(entered, open) || !open.IsSubMenuOpen) return false;
        if (!InCorridor(last, now, submenuScreen)) return false;
        ScheduleCloseIfAway(open);
        return true;
    }

    /// <summary>Closes <paramref name="open"/> after the close delay unless the pointer is back on it or its rows by then.</summary>
    internal void ScheduleCloseIfAway(MenuItem open)
    {
        var ticket = ++_closeTicket;
        DelayRunner(() =>
        {
            if (ticket != _closeTicket || !open.IsSubMenuOpen || IsPointerWithin(open)) return;
            open.IsSubMenuOpen = false;
        }, CloseDelay);
    }

    internal void CancelPendingClose() => _closeTicket++;

    /// <summary>True when the pointer at <paramref name="now"/> lies in the triangle from <paramref name="last"/> to the near edge of the submenu.</summary>
    internal static bool InCorridor(PixelPoint last, PixelPoint now, Rect submenu)
    {
        if (submenu.Width <= 0 || submenu.Height <= 0) return false;
        var nearX = last.X <= submenu.Left ? submenu.Left : submenu.Right;
        var a = new Point(last.X, last.Y);
        var b = new Point(nearX, submenu.Top);
        var c = new Point(nearX, submenu.Bottom);
        var p = new Point(now.X, now.Y);
        var d1 = Cross(a, b, p);
        var d2 = Cross(b, c, p);
        var d3 = Cross(c, a, p);
        var hasNegative = d1 < 0 || d2 < 0 || d3 < 0;
        var hasPositive = d1 > 0 || d2 > 0 || d3 > 0;
        return !(hasNegative && hasPositive);
    }

    private static double Cross(Point a, Point b, Point p) => (a.X - p.X) * (b.Y - p.Y) - (b.X - p.X) * (a.Y - p.Y);

    // True while the pointer is over the item or over any row of its (nested) submenus.
    internal static bool IsPointerWithin(MenuItem item) =>
        item.IsPointerOver || item.Items.OfType<MenuItem>().Any(IsPointerWithin);

    private static MenuItem? OpenSiblingOf(MenuItem entered) =>
        entered.Parent is ItemsControl owner ? owner.Items.OfType<MenuItem>().FirstOrDefault(i => i.IsSubMenuOpen && !ReferenceEquals(i, entered)) : null;

    // The outermost open submenu parent in the chain above the item that has its submenu open (the row itself, or an ancestor row).
    private static MenuItem? OpenRootOf(MenuItem item)
    {
        MenuItem? found = item.IsSubMenuOpen ? item : null;
        for (var parent = item.Parent as MenuItem; parent is not null; parent = parent.Parent as MenuItem)
            if (parent.IsSubMenuOpen) found = parent;
        return found;
    }

    private static Rect SubmenuScreenRect(MenuItem open)
    {
        var popup = open.GetVisualDescendants().OfType<Popup>().FirstOrDefault(p => p.Name == "PART_Popup");
        if (popup?.Child is not Visual child) return default;
        var topLeft = child.PointToScreen(new Point(0, 0));
        return new Rect(topLeft.X, topLeft.Y, child.Bounds.Width, child.Bounds.Height);
    }
}
