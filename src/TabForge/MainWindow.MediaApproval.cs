using System.Windows;
using TabForge.Views;

namespace TabForge;

// MainWindow: the approval notices (linked audio, plug-ins) belong to ApprovalNoticeController, built by BackgroundServices (MainWindow.Autosave.cs);
// this file keeps the entry points the rest of the window and its Settings dialogs call.
// Owns: the window's entry points for approval notices (linked audio and plug-ins) and their bars in the Settings dialogs.
// Does not own: the notice logic (ApprovalNoticeController, built by BackgroundServices).
// Tests: listed in docs/feature-map/plug-ins.md.
public partial class MainWindow
{
    /// <summary>
    /// What this window's Settings dialogs call back into: bound to this window instance (no static Preferences actions), so a dialog opened
    /// from one window can only ever act on that window, whichever window was created last or has focus.
    /// </summary>
    private SettingsWindowActions CreateSettingsActions() => new()
    {
        ManageLinkedAudio = owner => { ReviewLinkedAudio(owner); return _settings.Audio.ApprovedMedia.ToList(); },
        ManageQuarantine = owner =>
        {
            Views.QuarantineWindow.Show(owner, () => _settings.Plugins.Quarantined.ToList(), path =>
            {
                if (TabForge.Plugins.PluginQuarantine.AllowAgain(_settings.Plugins.Quarantined, path) == 0) return;
                SaveSettings();
                SyncAudioEngine();   // the chain key no longer says Skip: the engine loads it again
                RefreshArrangement();   // the faulted FX icon clears
            });
            return _settings.Plugins.Quarantined.ToList();
        },
        EditGroupRules = _ => OpenGroupRules(),
        CheckForUpdatesNow = CheckForUpdatesFromSettings,   // Settings > General > Updates > Check now
        ShowAllTracksAs = view => InstrumentPane.SetInstrumentView(view, null),   // Settings > Fretboard > Show all tracks as
    };

    private void UpdateMediaApprovalBar() => _services.Approvals.UpdateMediaApprovalBar();

    private void UpdatePluginTrustBar() => _services.Approvals.UpdatePluginTrustBar();

    /// <summary>The "Linked audio" window for the song shown now, over <paramref name="owner"/> (this window when it is not visible).</summary>
    private void ReviewLinkedAudio(Window owner) =>
        LinkedAudioReviewWindow.Show(owner.IsVisible ? owner : this, _services.Approvals.BeginLinkedAudioReview());
}
