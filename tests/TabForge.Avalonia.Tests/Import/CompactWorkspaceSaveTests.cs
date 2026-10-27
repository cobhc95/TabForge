using System;
using System.IO;
using System.Text.Json;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using Xunit;

namespace TabForge.Avalonia.Tests;

/// <summary>
/// Owns: the saved Workspace while compact mode or the Band layout is on: it keeps the layout from before the mode (the hidden panes are not
/// saved), and the restored layout is saved on exit.
/// Does not own: the debounced file write (AppSettingsStore), the layout rules (DockWorkspaceRules).
/// Tests: MainWindowSettings.HoldWorkspace, MainWindowSettings.SaveLayout.
/// </summary>
public class CompactWorkspaceSaveTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "tf-compact-save-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder)) Directory.Delete(_folder, recursive: true);
    }

    private static string Json(DockWorkspaceState? state) => JsonSerializer.Serialize(state);

    [Fact]
    public void WhileCompactModeIsOnTheSavedWorkspaceIsThePreCompactLayout()
    {
        using var settings = MainWindowSettings.Open(Path.Combine(_folder, "settings.json"));
        var before = DockWorkspaceRules.Capture(DockWorkspaceController.DefaultLayout());

        // Compact mode: hold the layout from before it, then the dock saves its hidden-pane state.
        settings.HoldWorkspace(before);
        var hidden = DockWorkspaceRules.Capture(before);
        DockWorkspaceRules.Close(hidden, "tools");
        DockWorkspaceRules.Close(hidden, "lyrics");
        settings.SaveLayout(hidden);
        Assert.Equal(Json(before), Json(settings.Settings.Workspace));

        // Exit: release the hold, the restored layout is what the dock saves.
        settings.HoldWorkspace(null);
        settings.SaveLayout(before);
        Assert.Equal(Json(before), Json(settings.Settings.Workspace));
    }
}
