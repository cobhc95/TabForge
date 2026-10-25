using Avalonia;
using TabForge.Shell;

namespace TabForge.Avalonia.Shell;

// Owns: the window that follows the pointer during a live song-tab tear-off: a new window holding a copy of the dragged song (the source
//   keeps the song listed and its tab hidden until release), or the source window itself when the song is its only tab. It keeps the
//   grabbed point under the pointer, turns translucent over another window's strip, and gives the song back on Escape or a pop-back.
//   The new window is shown without activation so the source keeps the pointer until release; the flow activates it when the song stays there.
// Does not own: the drag state (DragSession), the drop decisions (DocumentTabDragFlow), the song lists (MainWindowTabs).
// Tests: TabForge.Avalonia.Tests.TabTearOffTests (new window, follow, revert, activation handoff). A real window move on screen is not
//   headless: the positions and activation flags are checked as values only.

/// <summary>The window a song tab is torn off into while the drag runs. UI thread only.</summary>
internal sealed class DocumentTabLiveTearOff
{
    /// <summary>The opacity of the dragged window while it is over another window's tab strip.</summary>
    public const double OverTargetOpacity = 0.7;

    private readonly PixelPoint _grabInWindow;
    private readonly PixelPoint _sourceStart;

    private DocumentTabLiveTearOff(MainWindow window, bool isLone, PixelPoint grabInWindow, PixelPoint sourceStart)
    {
        Window = window;
        IsLone = isLone;
        _grabInWindow = grabInWindow;
        _sourceStart = sourceStart;
    }

    /// <summary>The window that follows the pointer: the new window, or the source itself when <see cref="IsLone"/>.</summary>
    public MainWindow Window { get; }

    /// <summary>True when the dragged song is the source window's only tab, so the source window itself moves.</summary>
    public bool IsLone { get; }

    /// <summary>Creates the tear-off of the song at <paramref name="index"/> with the pointer at <paramref name="screen"/>: the grabbed point
    /// of the tab stays under the pointer. The new window is not shown yet, so the drag flow can record it before it can deactivate the source.</summary>
    public static DocumentTabLiveTearOff Create(MainWindow source, int index, PixelPoint screen)
    {
        var grabScreen = source.TabStrip.Tabs[index].PointToScreen(source.TabStrip.DragGrabInTab ?? default);
        var grabInWindow = new PixelPoint(grabScreen.X - source.Position.X, grabScreen.Y - source.Position.Y);
        if (TabDragRules.TearOffAction(source.Songs.Songs.Count) == TabTearOffAction.MoveWindow)
            return new DocumentTabLiveTearOff(source, true, grabInWindow, source.Position);
        var torn = new MainWindow(source.Settings, source.Windows, source.Songs.Songs[index], source.Playback)
        {
            Position = Offset(screen, grabInWindow),
            RequestedThemeVariant = source.RequestedThemeVariant,
            // The source keeps activation and the pointer capture until release: the new window appears under the cursor and follows the
            // same gesture; Drop activates it when the song stays there.
            ShowActivated = false,
        };
        torn.Songs.SetTearCopy(true);   // until the drop, the source window owns the song's loop and transport
        return new DocumentTabLiveTearOff(torn, false, grabInWindow, source.Position);
    }

    /// <summary>Shows the created window without taking activation from the source (a lone tab's source is already shown).</summary>
    public void Show()
    {
        if (!IsLone) Window.Show();
    }

    /// <summary>Moves the followed window so the grabbed point is under the pointer at <paramref name="screen"/>.</summary>
    public void Follow(PixelPoint screen) => Window.Position = Offset(screen, _grabInWindow);

    /// <summary>Makes the followed window translucent while it is over another window's strip, opaque otherwise.</summary>
    public void SetOverTarget(bool overTarget) => Window.Opacity = overTarget ? OverTargetOpacity : 1;

    /// <summary>Gives the song back to the source window: a lone tab's window returns to where it started, a new window closes.</summary>
    public void Revert()
    {
        if (IsLone)
        {
            Window.Position = _sourceStart;
            Window.Opacity = 1;
            return;
        }
        CloseNewWindow();
    }

    /// <summary>Removes the copy of the song from the new window and closes it; the source window keeps its song. A lone tab's window is
    /// the source itself and is not closed here.</summary>
    public void CloseNewWindow()
    {
        if (IsLone) return;
        Window.Songs.Detach(0);
        Window.Close();
    }

    private static PixelPoint Offset(PixelPoint pointer, PixelPoint grab) => new(pointer.X - grab.X, pointer.Y - grab.Y);
}
