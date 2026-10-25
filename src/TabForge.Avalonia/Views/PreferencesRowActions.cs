using System;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace TabForge.Avalonia.Views;

// Owns: the actions the Settings window's button rows call on the main window that opened it: the main window's commands by id,
//       and the linked-audio and plug-in quarantine reviews over the Settings window.
// Does not own: what each action does (the commands, MainWindowApprovalNotices) or the rows and their labels (PreferencesWindow.Buttons.cs).
// Tests: TabForge.Avalonia.Tests.PreferencesSettingsTransferTests.

/// <summary>What one main window offers the Settings rows. Built per window and passed to the Settings window it opens; nothing is static.</summary>
public sealed class PreferencesRowActions
{
    /// <summary>Runs a main window command by its id (Help.CheckForUpdates for Check now, Mixer.GroupRules for Group rules).</summary>
    public required Action<string> RunCommand { get; init; }

    /// <summary>Shows the linked-audio review over the given window and returns when it closes; approvals it changed are saved by the main window.</summary>
    public required Func<Window, Task> ManageLinkedAudio { get; init; }

    /// <summary>Shows the plug-ins switched off after a crash over the given window and returns when it closes.</summary>
    public required Func<Window, Task> ManageQuarantine { get; init; }

    /// <summary>Has the audio engine show the control panel of the named ASIO driver (empty = the first installed one); the task ends with why it could not (empty: it was shown). Null: this build cannot, and the Configure button stays off.</summary>
    public Func<string, Task<string>>? ConfigureAsio { get; init; }

    /// <summary>The ASIO driver the running audio engine plays through (null: none). Configure stays off for any other driver, because the engine holds its own.</summary>
    public Func<string?>? RunningAsioDevice { get; init; }

    /// <summary>Asks for plug-in folders over the given window; null or empty when cancelled. Null: the system folder picker is used.</summary>
    public Func<Window, string[]?>? PickFolders { get; init; }
}
