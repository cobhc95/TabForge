using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the handler of the Options menu row Stylesheet, which opens the Settings window as the WPF app does (its Stylesheet_Click
//   runs the same Prefs_Click as the Settings button: a plain open, so the first open shows Home and later opens return to the
//   last page, instead of pinning Home every time).
// Does not own: the menu row (MainMenuTable in Core), the commands table (MainWindowCommands), the Settings window (PreferencesWindow),
//   the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
/// <summary>The Options menu command ids this class handles.</summary>
public static class MainWindowOptionsMenu
{
    /// <summary>The Options menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Options.Stylesheet" };

    // The page the last Stylesheet open showed; null until the first one closes (WPF PreferencesWindow._lastPage parity, kept here
    // so the plain Stylesheet open remembers it without forcing Home again).
    private static string? _lastPage;

    /// <summary>The page a plain Settings open shows: the last page again, or Home before any (WPF TakeInitialCategory parity).</summary>
    internal static string ResolvePage(string? lastPage) => lastPage ?? SettingsCatalog.Home;

    /// <param name="commands">The command table the menu runs from.</param>
    /// <param name="owner">The window the Settings window opens over.</param>
    /// <param name="settings">The shared settings the Settings window shows; null shows the defaults.</param>
    public static void Register(MainWindowCommands commands, Window owner, MainWindowSettings? settings)
    {
        commands.Add("Options.Stylesheet", () => ShowKeepingPage(settings, owner));
    }

    /// <summary>Opens the Settings window on the last page again (Home first), recording the page it is closed on.</summary>
    private static void ShowKeepingPage(MainWindowSettings? settings, Window owner)
    {
        if (settings is null)
        {
            var plain = new PreferencesWindow(new AvaloniaDialogHost(), new AppSettings());
            plain.ShowPage(ResolvePage(_lastPage));
            plain.Show(owner);
            return;
        }
        settings.OpenPreferences(owner, () =>
        {
            var window = settings.CreatePreferencesWindow((owner as MainWindow)?.PreferencesActions);
            window.ShowPage(ResolvePage(_lastPage));
            window.Closed += (_, _) => _lastPage = window.Page;
            return window;
        });
    }
}
