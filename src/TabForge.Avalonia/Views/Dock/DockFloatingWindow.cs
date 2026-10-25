using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.MarkupExtensions;
using TabForge.Docking;
using TabForge.Platform;

namespace TabForge.Avalonia.Views.Docking;

// Owns: one floating dock window: it hosts the DockWorkspaceView of a floating root, hides on the user's X and reports it,
//   reports moves and resizes, and releases its content and listeners when the workspace closes it.
// Does not own: the floating state or what a close does to the layout (DockWorkspaceController, FloatingSessionRules).
// Tests: TabForge.Avalonia.Tests.DockFloatingWindowTests.
/// <summary>An owned, taskbar-less window for a floating dock root. Bounds are in device-independent pixels.</summary>
public sealed class DockFloatingWindow : Window
{
    private bool _closingFromWorkspace;

    public DockFloatingWindow(string id, DockWorkspaceView view)
    {
        Id = id;
        View = view;
        ShowInTaskbar = false;
        CanResize = true;
        WindowStartupLocation = WindowStartupLocation.Manual;
        this[!BackgroundProperty] = new DynamicResourceExtension("PanelBackground");
        Content = view;
        Closing += (_, e) =>
        {
            // The owner closing (or the app shutting down) closes this window too; only the window's own close is the user's X.
            if (_closingFromWorkspace || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            Hide();
            ClosingByUser?.Invoke(this);
        };
        PositionChanged += (_, _) => BoundsChanged?.Invoke(this);
        Resized += (_, _) => BoundsChanged?.Invoke(this);
    }

    public string Id { get; }

    public DockWorkspaceView View { get; }

    /// <summary>Raised after the user's X hid the window.</summary>
    public event Action<DockFloatingWindow>? ClosingByUser;

    /// <summary>Raised when the window moved or changed size.</summary>
    public event Action<DockFloatingWindow>? BoundsChanged;

    /// <summary>True once the workspace closed the window: no content and no listeners are left. Exists for the tests to check the release.</summary>
    public bool IsReleased => Content is null && ClosingByUser is null && BoundsChanged is null;

    /// <summary>The window's place and client size in DIPs.</summary>
    public UiRect DipBounds => new(Position.X / DesktopScaling, Position.Y / DesktopScaling, ClientSize.Width, ClientSize.Height);

    /// <summary>Moves and sizes the window to the saved floating bounds.</summary>
    public void Place(DockFloatingState floating)
    {
        Width = floating.Width;
        Height = floating.Height;
        Position = new PixelPoint((int)Math.Round(floating.Left * DesktopScaling), (int)Math.Round(floating.Top * DesktopScaling));
    }

    /// <summary>Closes the window for good and lets go of its content, so neither keeps the other alive.</summary>
    public void CloseFromWorkspace()
    {
        _closingFromWorkspace = true;
        ClosingByUser = null;
        BoundsChanged = null;
        Content = null;
        Close();
    }
}
