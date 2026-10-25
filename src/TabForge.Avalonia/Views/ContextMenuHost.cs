using System;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

/// <summary>Runs the commands a right-click menu row names, by command id.</summary>
public interface IMenuCommandRunner
{
    /// <summary>True when the window has a handler for this command id; a row with no handler renders disabled.</summary>
    bool Handles(string commandId);

    /// <summary>Runs the command; an id with no handler throws, because the menu never enables one.</summary>
    void Run(string commandId, string? argument = null);
}

// Owns: what a window hands its right-click menus once it has built its commands and its clipboard: the command runner, and the clipboard whose
//   CanPaste enables the paste rows. Until the window sets them, no row runs and no paste row is enabled.
//   The views keep the host instance they were given, so the window may set its parts after the views are built.
// Does not own: the rows (ContextMenuRows), the items (ContextMenuBuilder), the handlers (MainWindowCommands), the clipboard (ClipboardService).
// Tests: TabForge.Avalonia.Tests.ContextMenuTests.
public sealed class ContextMenuHost
{
    /// <summary>The runner of a host with no commands: every row renders disabled.</summary>
    public static readonly IMenuCommandRunner NoCommands = new NoCommandRunner();

    /// <summary>The window's command table; <see cref="NoCommands"/> until the window sets it.</summary>
    public IMenuCommandRunner Commands { get; set; } = NoCommands;

    /// <summary>The window's clipboard; null until the window sets it, so the paste rows stay disabled.</summary>
    public ClipboardService? Clipboard { get; set; }

    /// <summary>True when the clipboard holds TabForge notes or a copied track to paste (the same state the WPF menus read).</summary>
    public bool CanPaste => (Clipboard?.CanPaste ?? false) || (Clipboard?.HasTrack ?? false);

    /// <summary>The bar under the last timeline right-click (set when its menu is built), or -1 when the pointer was on no bar.</summary>
    public int ClickedBar { get; set; } = -1;

    /// <summary>Moves the playhead (the transport) to a bar; the arrangement sets it, so the ruler menu's Move playhead here seeks the playback.</summary>
    public Action<int>? MovePlayhead { get; set; }

    /// <summary>The track row under the last track row right-click (set when its menu is built), or -1 when no track row was under the pointer.</summary>
    public int ClickedTrack { get; set; } = -1;

    /// <summary>The audio clip under the last timeline right-click when its file could not be read (the Retry row reads it), else null.</summary>
    public TabForge.Models.AudioClip? ClickedUnreadableClip { get; set; }

    /// <summary>The index in the song's markers of the section under the last timeline right-click (its bracket, or the section holding the bar), or -1 when none.</summary>
    public int ClickedSection { get; set; } = -1;

    /// <summary>The live hotkey settings; the undo hint of a confirm prompt reads the key of Edit.Undo from them.</summary>
    public Func<TabForge.Services.HotkeySettings> Hotkeys { get; set; } = () => new TabForge.Services.HotkeySettings();

    private sealed class NoCommandRunner : IMenuCommandRunner
    {
        public bool Handles(string commandId) => false;

        public void Run(string commandId, string? argument) => throw new InvalidOperationException($"No handler for command '{commandId}'.");
    }
}
