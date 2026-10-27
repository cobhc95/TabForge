using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using TabForge.Menus;
using TabForge.Presenters;

namespace TabForge.Avalonia;

// Owns: the table of main-menu command id -> handler for the Avalonia window: the window's own commands (Exit, reset panels) and
//   the preview dialogs that a main-menu row opens (PreviewHeaderOfCommand). Which entries of the preview list no row claims.
// Does not own: the menu rows (MainMenuTable in Core), the menu items (MainWindowMenu), the dialogs (PreviewToolsMenu and Views).
//   An id with no handler here is shown disabled by the menu; the Avalonia window gains handlers as the features are ported.
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests.
/// <summary>Command id to handler. Built once per window; read on the UI thread only.</summary>
public sealed class MainWindowCommands : TabForge.Avalonia.Views.IMenuCommandRunner
{
    private readonly Dictionary<string, Action> _handlers = new(StringComparer.Ordinal);

    /// <summary>The main-menu command ids whose row opens a preview dialog, by the dialog's caption (PreviewToolsMenu). The dialog opens with the
    /// preview's sample state, as the other preview entries do; a row not listed here stays disabled.</summary>
    private static readonly IReadOnlyDictionary<string, string> PreviewHeaderOfCommand = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["App.Preferences"] = "Settings…",
        ["View.LearnPopout"] = "Keyboard mode: view…",
    };

    /// <summary>Preview dialogs the Avalonia app already opens from their own place (a flow or a control, not a menu row), so the Tools submenu does not list them.</summary>
    private static readonly IReadOnlySet<string> ReachableFromTheirOwnPlace = new HashSet<string>(StringComparer.Ordinal)
    {
        "Project settings…", "Save changes prompt…", "Discard changes prompt…", "Paste options…", "Paste special…", "Group rules…", "MIDI output…",
        "Export video (MP4)…", "Export .gp file…",
    };

    /// <param name="owner">The window dialogs open over and Exit closes.</param>
    /// <param name="resetPanels">Puts the dock panels back to their default positions.</param>
    public MainWindowCommands(Window owner, Action resetPanels)
    {
        _handlers["File.Exit"] = owner.Close;
        _handlers["View.ResetPanels"] = resetPanels;
        var previews = PreviewToolsMenu.Entries(owner);
        var claimed = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (commandId, header) in PreviewHeaderOfCommand)
        {
            var preview = previews.FirstOrDefault(p => p.Header == header);
            if (preview is null) continue;
            _handlers[commandId] = preview.Open;
            claimed.Add(preview.Header);
        }
        Unclaimed = previews.Where(p => !claimed.Contains(p.Header) && !ReachableFromTheirOwnPlace.Contains(p.Header)).ToList();
    }

    /// <summary>The preview dialogs no main-menu row names; the menu lists them at the end of Tools.</summary>
    public IReadOnlyList<PreviewDialogEntry> Unclaimed { get; }

    /// <summary>Adds the handler of a window part (tabs) before the menu is built; an id already handled throws.</summary>
    public void Add(string commandId, Action handler) => _handlers.Add(commandId, handler);

    /// <summary>The guards of the commands added with AddWhen, by command id. Read on the UI thread only.</summary>
    private readonly Dictionary<string, Func<bool>> _guards = new(StringComparer.Ordinal);

    /// <summary>Adds a handler that applies only while <paramref name="applies"/> is true. A key bound to it is used only then; otherwise the key
    /// falls through to the focused control (MainWindowKeyboard checks <see cref="Applies"/>).</summary>
    public void AddWhen(string commandId, Action handler, Func<bool> applies)
    {
        Add(commandId, handler);
        _guards[commandId] = applies;
    }

    /// <summary>The handlers that take the menu row's argument (the track row colour swatch's hex), by command id. Read on the UI thread only.</summary>
    private readonly Dictionary<string, Action<string>> _argumentHandlers = new(StringComparer.Ordinal);

    /// <summary>Adds a handler that takes the row's argument; the menu runs it with the row's Arg, and a Run without one throws.</summary>
    public void AddWithArgument(string commandId, Action<string> handler) => _argumentHandlers.Add(commandId, handler);

    /// <summary>The live on/off state of the checkable rows that follow a value (notation, score page, zoom), by command id. Read on the UI thread only.</summary>
    private readonly Dictionary<string, Func<bool>> _checkStates = new(StringComparer.Ordinal);

    /// <summary>Registers how a checkable row finds out whether it is the active choice; the menu reads it each time it opens.</summary>
    public void AddCheckState(string commandId, Func<bool> isOn) => _checkStates[commandId] = isOn;

    /// <summary>The live check of the row, or null when no state was registered for it.</summary>
    public bool? CheckStateOf(string commandId) => _checkStates.TryGetValue(commandId, out var isOn) ? isOn() : null;

    public bool Handles(string commandId) => _handlers.ContainsKey(commandId) || _argumentHandlers.ContainsKey(commandId);

    /// <summary>False when the command's guard (AddWhen) says it does not apply now; true for every command without a guard.</summary>
    public bool Applies(string commandId) => !_guards.TryGetValue(commandId, out var applies) || applies();

    /// <summary>Runs the command with the row's argument (null for a row without one); an id with no handler throws, because the menu never enables one.</summary>
    public void Run(string commandId, string? argument = null)
    {
        if (_argumentHandlers.TryGetValue(commandId, out var withArgument))
        {
            withArgument(argument ?? throw new InvalidOperationException($"Command '{commandId}' needs an argument."));
            return;
        }
        (_handlers.GetValueOrDefault(commandId) ?? throw new InvalidOperationException($"No handler for command '{commandId}'.")).Invoke();
    }
}
