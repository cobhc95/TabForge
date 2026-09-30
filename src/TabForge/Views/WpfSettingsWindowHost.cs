using System.Windows;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>Shows the native settings dialog on the owning WPF application's dispatcher.</summary>
public sealed class WpfSettingsWindowHost(Window owner) : ISettingsWindowHost
{
    public SettingsShowResult Show(AppSettings current, Action<AppSettings> apply, Action<AppSettings> preview)
    {
        var baseline = SettingsMigration.Clone(current);
        try
        {
            var dialog = new PreferencesWindow(current, owner, apply, preview);
            return DialogHost.ShowModal(dialog) == true ? SettingsShowResult.Applied : SettingsShowResult.Cancelled;
        }
        catch (Exception ex)
        {
            // Best effort: put the pre-dialog appearance back; the error below is what the user needs to see.
            try { preview(SettingsMigration.Clone(baseline)); }
            catch (Exception restoreError) { System.Diagnostics.Debug.WriteLine($"Settings preview restore failed: {restoreError}"); }
            MessageBox.Show(owner,
                $"Settings could not be opened.\n\n{ex.GetBaseException().Message}",
                "TabForge Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return SettingsShowResult.Failed;
        }
    }
}
