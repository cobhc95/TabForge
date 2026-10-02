using System.Windows;
using TabForge.Documents;

namespace TabForge.Shell;

/// <summary>A window's tab strip as seen by another window's held tear-off: where a tab may be dropped, the highlight, and taking the song over.</summary>
internal interface ITabTransferTarget
{
    /// <summary>Shown and not closed.</summary>
    bool IsOpen { get; }
    bool CanAcceptAttachAt(Point screenPoint);
    int AttachInsertIndexAt(Point screenPoint);
    void SetAttachHighlight(bool highlighted);
    /// <summary>Takes the song over at <paramref name="index"/>; false (nothing changed) when the window has closed.</summary>
    bool TryAdopt(DocumentSession session, int index);
}

/// <summary>Weak registry of compatible TabForge windows used only during a held tear-off.</summary>
internal static class TabWindowRegistry
{
    private static readonly object Gate = new();
    private static readonly List<WeakReference<ITabTransferTarget>> Windows = new();

    public static void Register(ITabTransferTarget window)
    {
        lock (Gate)
        {
            Cleanup();
            if (!Windows.Any(x => x.TryGetTarget(out var live) && ReferenceEquals(live, window)))
                Windows.Add(new WeakReference<ITabTransferTarget>(window));
        }
    }

    public static void Unregister(ITabTransferTarget window)
    {
        lock (Gate) Windows.RemoveAll(x => !x.TryGetTarget(out var live) || ReferenceEquals(live, window));
    }

    public static ITabTransferTarget? FindTarget(ITabTransferTarget source, Point screenPoint)
    {
        ITabTransferTarget[] windows;
        lock (Gate)
        {
            Cleanup();
            windows = Windows.Select(x => x.TryGetTarget(out var live) ? live : null)
                .Where(x => x is { IsOpen: true }).Cast<ITabTransferTarget>().ToArray();
        }
        return windows.FirstOrDefault(x => !ReferenceEquals(x, source) && x.CanAcceptAttachAt(screenPoint));
    }

    private static void Cleanup() => Windows.RemoveAll(x => !x.TryGetTarget(out _));
}
