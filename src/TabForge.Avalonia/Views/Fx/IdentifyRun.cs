namespace TabForge.Avalonia.Views.Fx;

// Owns: the states of the Add plug-in window's identify pass (what its Pause, Resume and Stop buttons act on).
// Does not own: the pass itself (PluginBrowserSession) or the buttons (PluginBrowserWindow).
// Tests: TabForge.Avalonia.Tests.PluginScanControlTests.
/// <summary>Where the identify pass is.</summary>
public enum IdentifyRun
{
    /// <summary>No pass is running.</summary>
    Idle,
    /// <summary>Probing plug-ins.</summary>
    Running,
    /// <summary>Waiting between plug-ins until Resume or Stop.</summary>
    Paused,
    /// <summary>Ended early; the rest stay unchecked until Rescan or reopening.</summary>
    Stopped,
}
