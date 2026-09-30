using System.Windows;
using TabForge.Views;

namespace TabForge.Shell;

/// <summary>Weak registry of compatible TabForge windows used only during a held tear-off.</summary>
internal static class TabWindowRegistry
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<MainWindow>> Windows = new();

    public static void Register(MainWindow window)
    {
        lock (Gate)
        {
            Cleanup();
            if (!Windows.Any(x => x.TryGetTarget(out var live) && ReferenceEquals(live, window)))
                Windows.Add(new WeakReference<MainWindow>(window));
        }
    }

    public static void Unregister(MainWindow window)
    {
        lock (Gate) Windows.RemoveAll(x => !x.TryGetTarget(out var live) || ReferenceEquals(live, window));
    }

    public static MainWindow? FindTarget(MainWindow source, Point screenPoint)
    {
        MainWindow[] windows;
        lock (Gate)
        {
            Cleanup();
            windows = Windows.Select(x => x.TryGetTarget(out var live) ? live : null)
                .Where(x => x is { IsVisible: true }).Cast<MainWindow>().ToArray();
        }
        return windows.FirstOrDefault(x => !ReferenceEquals(x, source) && x.CanAcceptTabAttachAt(screenPoint));
    }

    private static void Cleanup() => Windows.RemoveAll(x => !x.TryGetTarget(out _));
}
