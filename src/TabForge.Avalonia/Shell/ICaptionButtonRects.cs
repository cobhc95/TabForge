using System.Collections.Generic;
using TabForge.Platform;
using TabForge.Shell;

namespace TabForge.Avalonia.Shell;

// Owns: what a custom title bar tells the native hit test: its caption buttons, tabs, tab actions and caption, and how it shows
//       a native hover or press on a caption button.
// Does not own: the hit rules (Core/Shell/ChromeHitRules), the window procedure (Shell/Win32/NcHitTestHook) or the drawing.
// Tests: TabForge.Avalonia.Tests.NcHitTestHookTests.
/// <summary>A title bar the native hit test can ask. Bounds are window client coordinates in DIPs. UI thread only.</summary>
public interface ICaptionButtonRects
{
    IReadOnlyList<(UiRect Bounds, ChromeHit Kind)> CaptionButtons { get; }

    IReadOnlyList<UiRect> TabBounds { get; }

    IReadOnlyList<UiRect> TabActionBounds { get; }

    /// <summary>The draggable caption strip; the tabs and buttons inside it win over it.</summary>
    UiRect CaptionBounds { get; }

    /// <summary>Shows <paramref name="button"/> hot (null: none). Called from native non-client mouse messages.</summary>
    void SetHot(ChromeHit? button);

    /// <summary>Shows <paramref name="button"/> pressed (null: none).</summary>
    void SetPressed(ChromeHit? button);

    /// <summary>
    /// A native middle-click released on the empty caption. Windows sends the press as WM_NCMBUTTONDOWN because the hit test answers
    /// HTCAPTION there, so Avalonia sees no pointer event; the hook calls this only on Windows. The title bar opens a new tab when the
    /// Tabs middle-click setting says so; tabs, tab actions and caption buttons never route here.
    /// </summary>
    void NativeMiddleClick();

    /// <summary>False when the Tabs setting turns the title-bar middle-click off: the native press then does nothing.</summary>
    bool MiddleClickEnabled => true;
}
