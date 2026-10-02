using System.Windows;
using TabForge.Services;

namespace TabForge.Views;

/// <summary>
/// What one main window offers its Settings dialogs (the rows that open the Linked audio and quarantine windows, "Apply to all tracks").
/// Each window builds its own and hands it to its <see cref="WpfSettingsWindowHost"/>, so a dialog's actions are bound to the window that
/// opened it. Nothing here is static: closing one window cannot clear, replace or leak another's.
/// </summary>
internal sealed class SettingsWindowActions
{
    /// <summary>Opens the Linked audio window (above the given dialog) for the song of the window that built these actions, and returns the approvals as they are afterwards.</summary>
    public Func<Window, List<MediaApproval>>? ManageLinkedAudio { get; init; }
    /// <summary>Lists the plug-ins switched off after a crash (live settings) with Allow again; returns the list afterwards.</summary>
    public Func<Window, List<string>>? ManageQuarantine { get; init; }
    /// <summary>Applies the default instrument view to every track of the window's open song.</summary>
    public Action<string>? ShowAllTracksAs { get; init; }
    /// <summary>Runs the manual update check now (even with the automatic check off); its dialog is owned by the given Settings window.</summary>
    public Action<Window>? CheckForUpdatesNow { get; init; }
}

/// <summary>Shows the native settings dialog on the owning WPF application's dispatcher.</summary>
public sealed class WpfSettingsWindowHost : ISettingsWindowHost
{
    private readonly Window _owner;
    private readonly SettingsWindowActions? _actions;

    public WpfSettingsWindowHost(Window owner) : this(owner, null) { }

    internal WpfSettingsWindowHost(Window owner, SettingsWindowActions? actions)
    {
        _owner = owner;
        _actions = actions;
    }

    public SettingsShowResult Show(AppSettings current, Action<AppSettings> apply, Action<AppSettings> preview)
    {
        var baseline = SettingsMigration.Clone(current);
        try
        {
            PreferencesWindow dialog;
            using (TabForge.Views.SlowTrace.Measure("settings window construct", 0)) dialog = new PreferencesWindow(current, _owner, apply, preview, _actions);
            dialog.ContentRendered += (_, _) => TabForge.Views.SlowTrace.Mark("settings window first frame");
            return DialogHost.ShowModal(dialog) == true ? SettingsShowResult.Applied : SettingsShowResult.Cancelled;
        }
        catch (Exception ex)
        {
            // Best effort: put the pre-dialog appearance back; the error below is what the user needs to see.
            try { preview(SettingsMigration.Clone(baseline)); }
            catch (Exception restoreError) { System.Diagnostics.Debug.WriteLine($"Settings preview restore failed: {restoreError}"); }
            MessageBox.Show(_owner,
                $"Settings could not be opened.\n\n{ex.GetBaseException().Message}",
                "TabForge Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            return SettingsShowResult.Failed;
        }
    }
}
