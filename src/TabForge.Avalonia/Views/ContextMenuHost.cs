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

/// <summary>
/// What a right-click pointed at: the bar, the track row, the take lane and its song time, the section (an index into the song's markers) and an audio clip whose file could not be read.
/// A value of -1 (a clip of null) means none. Immutable: a menu builds a new one with <c>with</c> and hands it to <see cref="ContextMenuHost.Aim"/>.
/// </summary>
/// <param name="Bar">The bar under the last timeline right-click, or -1 when the pointer was on no bar.</param>
/// <param name="Track">The track row under the last right-click, or -1 when no track row was under the pointer.</param>
/// <param name="Lane">The clip lane under the last take-lane right-click (on <paramref name="Track"/>), or -1.</param>
/// <param name="Sec">The song time of the last take-lane right-click, snapped like a clip drag (the lane menu's paste, new clip, import and record-here place at it).</param>
/// <param name="Section">The index in the song's markers of the section under the last timeline right-click (its bracket, or the section holding the bar), or -1 when none.</param>
/// <param name="UnreadableClip">The audio clip under the last timeline right-click when its file could not be read (the Retry row reads it), else null.</param>
public sealed record MenuTarget(int Bar, int Track, int Lane, double Sec, int Section, TabForge.Models.AudioClip? UnreadableClip)
{
    /// <summary>Nothing clicked yet.</summary>
    public static readonly MenuTarget None = new(-1, -1, -1, 0, -1, null);
}

// Owns: what a window hands its right-click menus once it has built its commands and its clipboard: the command runner, and the clipboard whose
//   CanPaste enables the paste rows, and the one MenuTarget the last right-click pointed at. Until the window sets them, no row runs and no paste row is enabled.
//   The views keep the host instance they were given, so the window may set its parts after the views are built.
// Does not own: the rows (ContextMenuRows) that decide the target of each menu, the items (ContextMenuBuilder), the handlers (MainWindowCommands), the clipboard (ClipboardService).
// Tests: TabForge.Avalonia.Tests.ContextMenuTests.
public sealed class ContextMenuHost
{
    /// <summary>The runner of a host with no commands: every row renders disabled.</summary>
    public static readonly IMenuCommandRunner NoCommands = new NoCommandRunner();

    /// <summary>The window's command table; <see cref="NoCommands"/> until the window sets it.</summary>
    public IMenuCommandRunner Commands { get; set; } = NoCommands;

    /// <summary>The window's clipboard; null until the window sets it, so the paste rows stay disabled.</summary>
    public ClipboardService? Clipboard { get; set; }

    /// <summary>True when the clipboard holds TabForge notes or a copied track to paste (the same state the menus read).</summary>
    public bool CanPaste => (Clipboard?.CanPaste ?? false) || (Clipboard?.HasTrack ?? false);

    /// <summary>The stored "clicking an empty take lane shows" choice (EditingSettings.EmptyLaneClick), read when the take-lane menu is built.</summary>
    public Func<string> EmptyLaneClick { get; set; } = () => TabForge.Services.EmptyLaneChoice.Tab;

    /// <summary>What the last right-click on the timeline or a track row pointed at (set when its menu was built); the commands read it when a row runs.</summary>
    public MenuTarget Target { get; private set; } = MenuTarget.None;

    /// <summary>Makes <paramref name="target"/> the click the commands act on. A menu path passes <c>Target with { ... }</c>, so what it does not name keeps the earlier click's value.</summary>
    public void Aim(MenuTarget target) => Target = target;

    /// <summary>Moves the playhead (the transport) to a bar; the arrangement sets it, so the ruler menu's Move playhead here seeks the playback.</summary>
    public Action<int>? MovePlayhead { get; set; }

    /// <summary>The bars a hovered menu row outlines on the timeline (null when none); set by the menu, shown by the timeline.</summary>
    public (int First, int Last)? HoverBars
    {
        get => _hoverBars;
        set { if (_hoverBars == value) return; _hoverBars = value; HoverBarsChanged?.Invoke(); }
    }
    private (int First, int Last)? _hoverBars;

    /// <summary>Raised when <see cref="HoverBars"/> changes.</summary>
    public event Action? HoverBarsChanged;

    /// <summary>The live hotkey settings; the undo hint of a confirm prompt reads the key of Edit.Undo from them.</summary>
    public Func<TabForge.Services.HotkeySettings> Hotkeys { get; set; } = () => new TabForge.Services.HotkeySettings();

    private sealed class NoCommandRunner : IMenuCommandRunner
    {
        public bool Handles(string commandId) => false;

        public void Run(string commandId, string? argument) => throw new InvalidOperationException($"No handler for command '{commandId}'.");
    }
}
