using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using TabForge.Avalonia;
using TabForge.Avalonia.Views;
using TabForge.Menus;
using TabForge.Services;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: "Score settings..." from the score's right-click menu opens the Settings window on the Score page and asks for the Page layout row to be
//   brought into view (SC-13), as the WPF row does.
// Does not own: the Settings window's pages and rows (PreferencesWindow), the menu row (ScoreMenus), the opener (MainWindowSettings).
// Tests: ScoreSettingsRowTargetTests (this file).
public class ScoreSettingsRowTargetTests
{
    [AvaloniaFact]
    public void ScoreSettingsOpensOnThePageLayoutRowAndBringsItIntoView()
    {
        var owner = new Window { Width = 900, Height = 600 };
        owner.Show();
        MainWindowSettings.ShowPreferencesPage(null, owner, SettingsCatalog.Score, ScoreMenus.SettingsRow);
        var preferences = owner.OwnedWindows.OfType<PreferencesWindow>().Single();
        var row = preferences.EditorOf(ScoreMenus.SettingsRow);
        var requested = false;
        row.AddHandler(Control.RequestBringIntoViewEvent, (_, _) => requested = true);

        Dispatcher.UIThread.RunJobs();

        Assert.Equal(SettingsCatalog.Score, preferences.Page);
        Assert.True(requested, "the Page layout row was asked to come into view");
        preferences.Close();
        owner.Close();
    }
}
