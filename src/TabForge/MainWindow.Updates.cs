using System.Windows;
using TabForge.Services;

namespace TabForge;

// MainWindow: update check (Settings > General > Updates; Help > Check for updates); the check itself is UpdateCheckController, built by BackgroundServices (MainWindow.Autosave.cs).
public partial class MainWindow
{
    private async void CheckForUpdates_Click(object sender, RoutedEventArgs e) => await CheckForUpdatesAsync(manual: true);

    /// <summary>Settings > General > Updates > Check now: the manual check (works with the automatic check off); its dialog is owned by the Settings window.</summary>
    private async void CheckForUpdatesFromSettings(Window owner)
    {
        _services.DialogOwner = owner;
        try { await CheckForUpdatesAsync(manual: true); }
        finally { _services.DialogOwner = null; }
    }

    private Task CheckForUpdatesAsync(bool manual) => _services.Update.CheckForUpdatesAsync(manual);
}
