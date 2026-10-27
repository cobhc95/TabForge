namespace TabForge.Avalonia.Views.Fx;

// Owns: the plug-ins last copied or cut in an FX chain window, as the text ChainClip made of them (plug-ins with their saved settings), kept for the main window so a
//   paste works in the FX window of another track too. It is the app's own clipboard: nothing is put on the system clipboard.
// Does not own: making the text or the plug-ins from it (ChainClip), the copy and paste commands (FxChainWindow.Clipboard.cs).
// Tests: TabForge.Avalonia.Tests.FxChainClipboardTests.
// Threads: UI thread only. Key: none (one value); ceiling: one snapshot, replaced by the next copy.
public sealed class FxPluginClipboard
{
    /// <summary>The copied plug-ins as text, or null while nothing was copied.</summary>
    public string? Snapshot { get; set; }

    public bool HasContent => Snapshot is not null;
}
