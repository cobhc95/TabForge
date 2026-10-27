using System.Collections.Generic;
using System;
using Avalonia.Controls;

namespace TabForge.Avalonia;

// Owns: the Help > Check for updates command, and the automatic check scheduled when the window opens.
// Does not own: the check run and its status texts (MainWindowUpdateCheckFlow), the request and the version rules (UpdateService in Core),
//   the answer dialog (Views/UpdateAvailableWindow).
// Tests: TabForge.Avalonia.Tests.HelpMenuTests.
/// <summary>Adds the Help > Check for updates handler to the window's command table and schedules the automatic check.</summary>
public static class MainWindowUpdateCheck
{
    /// <summary>The Help menu command id this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Help.CheckForUpdates" };

    /// <summary>
    /// Registers the handler and the automatic check; <paramref name="settings"/> holds the check time and the opt-out (null: nothing is recorded
    /// and no automatic check runs); <paramref name="status"/> shows the status texts.
    /// </summary>
    public static void Register(MainWindowCommands commands, Window owner, MainWindowSettings? settings, Action<string> status)
    {
        var flow = new MainWindowUpdateCheckFlow(owner, settings, status);
        commands.Add("Help.CheckForUpdates", () => _ = flow.RunAsync(manual: true));
        owner.Opened += (_, _) => flow.ScheduleAutomaticCheck();
    }
}
