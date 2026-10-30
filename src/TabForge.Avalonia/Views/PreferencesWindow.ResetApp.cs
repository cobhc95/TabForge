using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia;
using TabForge.Platform;
using TabForge.Services.Reset;

namespace TabForge.Avalonia.Views;

// Owns: the Advanced page's "Reset entire app" flow: count off the UI thread, show the two-step dialog, then write the
//   pending-wipe marker after the main window has actually closed (so a cancelled close never wipes anything).
// Does not own: the plan/wipe (Core Reset namespace), the dialog's controls (ResetAppWindow) or the Settings rows.
// Tests: ResetAppDialogTests, ResetAppPlanTests.
public sealed partial class PreferencesWindow
{
    private async Task OpenResetAppAsync()
    {
        _status.Text = "Counting app files…";
        ResetAppPlan plan;
        try
        {
            plan = await Task.Run(() => ResetAppPlan.Create(AppFolders.Current));
        }
        catch (Exception ex)
        {
            _status.Text = "The app folders could not be counted: " + ex.Message;
            return;
        }

        var dialog = new ResetAppWindow(plan);
        bool confirmed;
        try
        {
            confirmed = await dialog.ShowDialog<bool>(this);
        }
        catch (Exception ex)
        {
            _status.Text = "The reset could not start: " + ex.Message;
            return;
        }
        if (!confirmed) { _status.Text = ""; return; }

        var root = (Window)this;
        while (root.Owner is Window owner) root = owner;
        void WriteMarker()
        {
            try { PendingAppWipe.Write(AppFolders.Current, dialog.SelectedKinds, dialog.BackupWritten); }
            catch (Exception ex) { TabForge.Services.Trace.Error(TabForge.Services.Trace.Ui, "reset app: write marker: " + ex.Message); }
        }
        // The main window may hold the close for the unsaved-song question: write only after it is approved, so keeping a
        // song (a cancelled close) leaves no marker to wipe on a later normal close.
        if (root is MainWindow { Lifetime: { } lifetime }) lifetime.AfterApprovedClose = WriteMarker;
        else root.Closed += (_, _) => WriteMarker();
        root.Close();
    }
}
