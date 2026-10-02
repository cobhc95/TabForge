namespace TabForge.Diagnostics;

/// <summary>
/// The command-line probes that drive the real main window (scripted capture, screenshot tour, menu, settings, recording, follow and update probes).
/// They see the window only through <see cref="MainWindow.ProbeAccess"/>; nothing of the window is widened for them.
/// </summary>
internal sealed partial class WindowProbes : MainWindow.ProbeAccess
{
    public WindowProbes(MainWindow window) : base(window) { }
}
