using System;
using System.IO;
using TabForge.Docking;
using TabForge.Services;

namespace TabForge;

// Owns: settings store and settings file safety checks (a change during a save, a bad workspace in an otherwise good file).
// Does not own: the settings snapshot bytes (TestSettingsFileSplitSnapshots) and the security bounds (TestSecurityInputBoundaries).
// Tests: TestSettingsChangeDuringSaveStaysPending, TestSettingsBadWorkspaceKeepsOtherSettings.
public static partial class SelfTest
{
    /// <summary>A change made while a save is writing the file stays pending and is written by the next save.</summary>
    private static void TestSettingsChangeDuringSaveStaysPending()
    {
        var root = Path.Combine(Path.GetTempPath(), "tf-settings-race-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "settings.json");
            using var store = AppSettingsStore.Open(file, TimeSpan.FromHours(1));
            store.Settings.WindowWidth = 1_000;
            store.MarkChanged();
            var once = true;
            store.BeforeWriteForTest = () =>
            {
                if (!once) return;
                once = false;
                store.Settings.WindowWidth = 1_200;
                store.MarkChanged();   // a change during the write
            };
            store.Flush();
            Check("settings change during a save stays pending", store.HasPendingSave);
            Check("settings change during a save is written by the next save",
                store.Flush() && !store.HasPendingSave && SettingsFileService.Load(file).WindowWidth == 1_200);
        }
        finally { TryDelete(root);
 }
    }

    /// <summary>A settings file whose workspace layout is invalid keeps every other setting; only the layout falls back to the default.</summary>
    private static void TestSettingsBadWorkspaceKeepsOtherSettings()
    {
        var root = Path.Combine(Path.GetTempPath(), "tf-settings-workspace-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "settings.json");
            var settings = new AppSettings { WindowWidth = 1_234, Workspace = new DockWorkspaceState { Root = new DockNodeState { Kind = "nonsense" } } };
            settings.Hotkeys.Bindings["File.Save"] = "Ctrl+Shift+F12";
            File.WriteAllText(file, System.Text.Json.JsonSerializer.Serialize(settings));
            using var store = AppSettingsStore.Open(file, TimeSpan.FromHours(1));
            Check("bad workspace: the file loads (LoadFailed false)", !store.LoadFailed);
            Check("bad workspace, good hotkeys: hotkeys and other settings kept, workspace dropped",
                store.Settings.Workspace is null && store.Settings.WindowWidth == 1_234 &&
                store.Settings.Hotkeys.Bindings.TryGetValue("File.Save", out var key) && key == "Ctrl+Shift+F12");
            store.MarkChanged();
            Check("bad workspace: saving is allowed", store.Flush() && SettingsFileService.Load(file).WindowWidth == 1_234);
        }
        finally { TryDelete(root);
 }
    }
}
