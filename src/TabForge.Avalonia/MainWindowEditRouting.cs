using System;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Edit.Copy, Edit.Cut, Edit.Paste and Edit.PasteSpecial handlers: each asks EditRouting which command the current focus target runs and runs it;
//   the score selection runs the score clipboard flow (MainWindowClipboard); the timeline range has its own copy, cut, paste and paste special ids (MainWindowRangeDelete, MainWindowRangePaste). The menu, the palette,
//   the toolbar and the context menus all run these ids, so none of them has a path of its own.
// Does not own: the routing table (Core EditRouting), the key tables (MainWindowKeyboard), the clip and track row commands themselves
//   (MainWindowClipKeys, MainWindowTrackRowEdits, which asks before a track goes), the clipboard flow (MainWindowClipboard).
//   A piano roll with focus is asked first (its notes, its own clipboard).
// Tests: TabForge.Avalonia.Tests.EditKeyRoutingTests, PianoRollViewTests.
/// <summary>Registers the focus-routed Edit.* handlers over the window's clipboard flow. UI thread only.</summary>
public sealed class MainWindowEditRouting
{
    private readonly MainWindowCommands _commands;
    private readonly MainWindowClipboard _flow;
    private readonly EditRouting _routing;
    private readonly EditTargets _targets;
    private readonly Func<EditVerb, bool>? _first;

    private MainWindowEditRouting(MainWindowCommands commands, MainWindowClipboard flow, EditRouting routing, EditTargets targets, Func<EditVerb, bool>? first)
    {
        _first = first;
        _commands = commands;
        _flow = flow;
        _routing = routing;
        _targets = targets;
    }

    /// <summary>Adds Edit.Copy, Edit.Cut, Edit.Paste and Edit.PasteSpecial; <paramref name="report"/> shows a failed paste. <paramref name="first"/> is asked
    /// before the routing table (the piano roll takes the verb while it has focus); true when it ran the verb.</summary>
    public static void Register(MainWindowCommands commands, MainWindowClipboard flow, EditRouting routing, EditTargets targets, Action<string> report, Func<EditVerb, bool>? first = null)
    {
        var router = new MainWindowEditRouting(commands, flow, routing, targets, first);
        commands.Add("Edit.Copy", () => router.Run("Edit.Copy", EditVerb.Copy, () => flow.Copy()));
        commands.Add("Edit.Cut", () => router.Run("Edit.Cut", EditVerb.Cut, () => flow.Cut()));
        commands.Add("Edit.Paste", () => router.Run("Edit.Paste", EditVerb.Paste, () => Platform.UiTasks.Forget(flow.PasteAsync(), "Paste", report)));
        commands.Add("Edit.PasteSpecial", () => router.Run("Edit.PasteSpecial", EditVerb.PasteSpecial, () => Platform.UiTasks.Forget(flow.PasteSpecialAsync(), "Paste special", report)));
    }

    // The routed id is this command's own for the score and the range (the clipboard flow runs), else another command (a clip or track row one).
    private void Run(string ownId, EditVerb verb, Action clipboardFlow)
    {
        if (_first?.Invoke(verb) == true) return;
        var selection = _targets.Selection;
        var focus = _targets.Focus;
        var routed = _routing.Resolve(focus, State(selection), verb);
        if (routed.CommandId is { } id && id != ownId)
        {
            if (_commands.Handles(id) && _commands.Applies(id)) _commands.Run(id);
            return;
        }
        if (focus is FocusTarget.None or FocusTarget.ScoreSelection or FocusTarget.Loop) clipboardFlow();
    }

    private EditSelectionState State(SelectionModel selection) => new(
        HasScoreSelection: selection.HasScoreSelection,
        HasRange: selection.BarRange is not null,   // 25 Oct: the loop is never a bar-command target
        HasClips: selection.ClipCount > 0,
        HasTrackRow: selection.TrackRowCount > 0,
        HasHighlight: _targets.Highlight.Has,
        ClipboardHasBars: true,   // the score flow reads the clipboard itself
        ClipboardHasClips: ClipClipboard.HasClip,
        ClipboardHasTrack: _flow.Clipboard.HasTrack,
        ClipboardHasRange: _flow.Clipboard.TryGetRange() is not null);
}
