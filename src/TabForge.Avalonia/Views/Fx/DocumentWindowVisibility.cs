using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Documents;

namespace TabForge.Avalonia.Views.Fx;

// Owns: the one rule for per-song floating windows: on a tab switch the other songs' windows hide (Hide, never Close: the plug-in stays loaded) and come back when their song is shown again.
// Does not own: which windows exist (FxChainWindows) or closing them with their song (FxChainWindows.CloseSong).
// Tests: TabForge.Avalonia.Tests.DocumentWindowVisibilityTests.
// Threads: UI thread only. Key: the window object; ceiling: the windows hidden by a tab switch.
public sealed class DocumentWindowVisibility
{
    private readonly HashSet<Window> _hidden = new();

    /// <summary>Hides the windows of songs other than <paramref name="shown"/> that are open; shows the ones this hid earlier when their song is shown.</summary>
    public void Apply(IEnumerable<(SongDocument Document, Window Window)> windows, SongDocument shown)
    {
        foreach (var (document, window) in windows)
        {
            if (!ReferenceEquals(document, shown)) { if (window.IsVisible) { _hidden.Add(window); window.Hide(); } }
            else if (_hidden.Remove(window)) window.Show();
        }
    }

    /// <summary>A window closed: forget it.</summary>
    public void Forget(Window window) => _hidden.Remove(window);
}
