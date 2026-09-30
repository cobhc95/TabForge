using System.Windows;

namespace TabForge.Views;

/// <summary>Hands focus back to an owned window's owner when it closes, so Windows does not minimise the main window.</summary>
internal static class OwnerActivation
{
    public static void Attach(Window window)
    {
        Window? owner = null;
        window.Closing += (_, e) => { if (!e.Cancel) owner = window.Owner; };
        window.Closed += (_, _) =>
        {
            var target = owner;
            owner = null;
            if (target is { IsLoaded: true, IsVisible: true } && target.WindowState != WindowState.Minimized && !target.IsActive)
                target.Dispatcher.BeginInvoke(() => { if (target.IsVisible && !target.IsActive) target.Activate(); });
        };
    }
}
