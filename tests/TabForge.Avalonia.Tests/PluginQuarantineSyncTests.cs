using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using TabForge.Avalonia.Views;
using TabForge.Presenters;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Quarantine rules: Allow again syncs the engine once (the plug-in loads again) and nothing when the path was not listed; Preferences keeps the live
/// quarantine list when it copies a snapshot over the live settings.
/// Owns: these checks. Does not own: the approval notices (MainWindowApprovalNotices), the copy (PreferencesCatalog).
/// Tests: this class.
/// </summary>
public class PluginQuarantineSyncTests
{
    private const string Crashy = "C:\\Plugins\\Crashy.dll";

    [AvaloniaFact]
    public void AllowAgainSyncsTheEngineOnceAndNothingWhenThePathWasNotListed()
    {
        var tabs = new MainWindowTabs(MainWindowTabs.NewSong(), _ => Task.FromResult(PromptButton.Cancel), _ => Task.FromResult(false));
        var synced = 0;
        var notices = new MainWindowApprovalNotices(new Window(), tabs, null, () => synced++, new TabForge.Plugins.PluginTrust());
        var quarantined = new List<string> { Crashy };
        Assert.True(notices.AllowAgain(quarantined, Crashy));
        Assert.Empty(quarantined);
        Assert.Equal(1, synced);
        Assert.False(notices.AllowAgain(quarantined, Crashy));
        Assert.Equal(1, synced);
    }

    [Fact]
    public void CopyingPreferencesKeepsTheLiveQuarantineList()
    {
        var live = new AppSettings();
        live.Plugins.Quarantined.Add(Crashy);
        var snapshot = new AppSettings();   // taken before the crash, so it lists nothing
        PreferencesCatalog.CopyInto(snapshot, live, includeWorkspace: false);
        Assert.Equal(new[] { Crashy }, live.Plugins.Quarantined);
    }
}
