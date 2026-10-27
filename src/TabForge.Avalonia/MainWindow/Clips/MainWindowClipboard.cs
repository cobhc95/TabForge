using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Edit menu's Copy, Cut, Paste and Paste Special for the Avalonia window: fetching the system clipboard before a paste,
//   opening the Paste Special window, and running the score editor's clip calls on the shown song.
// Does not own: the clip format and capture (Core ScoreClip, ClipboardService), the paste rules (PasteCommands), the cursor and selection
//   (ScorePreviewEditor), the system clipboard (AvaloniaScoreClipboard), the paste questions (PasteQuestionFlow, Views.PasteOptionsWindow),
//   the whole-bar cut (Core WholeBarCut; its clip question takes the recommended answer: no question window yet, undo restores what went).
// Tests: TabForge.Avalonia.Tests.MainWindowClipboardTests.
/// <summary>Registers the clipboard commands. Built once per window.</summary>
public sealed class MainWindowClipboard
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Edit.Copy", "Edit.Cut", "Edit.Paste", "Edit.PasteSpecial" };

    private readonly Func<ScorePreviewView> _active;
    private ScorePreviewView _score => _active();
    private readonly AvaloniaScoreClipboard _system;
    private readonly Func<EditingSettings> _settings;
    private readonly Func<bool, Task<PasteSpecialOptions?>> _askPasteSpecial;
    private readonly Func<IReadOnlyCollection<PasteQuestion>, Task<PasteAnswers?>> _askPasteQuestions;

    /// <summary>The score clipboard of this window (shares the system clipboard with every TabForge instance).</summary>
    internal ClipboardService Clipboard { get; }

    internal MainWindowClipboard(ScorePreviewView score, AvaloniaScoreClipboard system, Func<EditingSettings> settings, Func<bool, Task<PasteSpecialOptions?>> askPasteSpecial,
        Func<IReadOnlyCollection<PasteQuestion>, Task<PasteAnswers?>>? askPasteQuestions = null, ClipboardService? clipboard = null)
        : this(() => score, system, settings, askPasteSpecial, askPasteQuestions, clipboard)
    {
    }

    /// <param name="active">The score view the clipboard acts on now (the main score, or a Band card's staff).</param>
    internal MainWindowClipboard(Func<ScorePreviewView> active, AvaloniaScoreClipboard system, Func<EditingSettings> settings, Func<bool, Task<PasteSpecialOptions?>> askPasteSpecial,
        Func<IReadOnlyCollection<PasteQuestion>, Task<PasteAnswers?>>? askPasteQuestions = null, ClipboardService? clipboard = null)
    {
        _askPasteQuestions = askPasteQuestions ?? (_ => Task.FromResult<PasteAnswers?>(new PasteAnswers()));   // no window: the recommended answers
        _active = active;
        _system = system;
        _settings = settings;
        _askPasteSpecial = askPasteSpecial;
        Clipboard = clipboard ?? new ClipboardService(system);
    }

    /// <summary>Adds Edit.Copy, Edit.Cut, Edit.Paste and Edit.PasteSpecial over <paramref name="owner"/>'s clipboard and dialogs; <see cref="MainWindowEditRouting"/> routes them by the focus target of the shown document's <paramref name="targets"/>.</summary>
    public static MainWindowClipboard Register(MainWindowCommands commands, Func<ScorePreviewView> score, Window owner, Func<EditingSettings> settings, Action<string> report,
        EditRouting routing, EditTargets targets, Shell.TabWindowRegistry windows, Func<EditVerb, bool>? first = null)
    {
        var flow = new MainWindowClipboard(score, windows.SystemClipboard, settings,
            bars => DialogLauncher.Ask<Views.PasteSpecialWindow, PasteSpecialOptions?>(owner, () => new Views.PasteSpecialWindow(bars)),
            questions => DialogLauncher.Ask<Views.PasteOptionsWindow, PasteAnswers?>(owner, () => new Views.PasteOptionsWindow(questions)),
            clipboard: windows.Clipboard);
        MainWindowEditRouting.Register(commands, flow, routing, targets, report, first);
        return flow;
    }

    /// <summary>Copies the selection (or the cursor beat); false when nothing could be captured.</summary>
    internal bool Copy()
    {
        if (_score.Editor.CaptureClip() is not { } clip) return false;
        Clipboard.Copy(clip);
        return true;
    }

    /// <summary>Copies, then clears what was copied (whole bars: removes them, WholeBarCut) as one undo step; false when nothing changed.</summary>
    internal bool Cut()
    {
        if (_score.Editor.CaptureClip() is not { } clip) return false;
        Clipboard.Copy(clip);
        return _score.Edit(editor => editor.CutClear(clip, new RecommendedBarDeleteAnswers()));
    }

    internal async Task<bool> PasteAsync()
    {
        if (await ClipToPaste() is not { } clip) return false;
        var settings = _settings();
        return await PasteQuestionFlow.RunAsync(asker =>
        {
            PasteOutcome? outcome = null;
            _score.Edit(editor => (outcome = editor.Paste((doc, target) => PasteCommands.RunPaste(doc, clip, target, settings, asker))).Changed);
            return outcome;
        }, _askPasteQuestions);
    }

    internal async Task<bool> PasteSpecialAsync()
    {
        if (await ClipToPaste() is not { } clip) return false;
        if (await _askPasteSpecial(clip.Kind == ScoreClipKind.Bars) is not { } options) return false;
        var settings = _settings();
        return _score.Edit(editor => editor.Paste((doc, target) => PasteCommands.RunPasteSpecial(doc, clip, target, options, settings)).Changed);
    }

    /// <summary>The copied timeline range to paste (after fetching the system clipboard), or null when the newest copy was not a range.</summary>
    internal async Task<RangeSnapshot?> RangeToPasteAsync()
    {
        await _system.RefreshAsync();
        return Clipboard.TryGetRange();
    }

    private async Task<ScoreClip?> ClipToPaste()
    {
        await _system.RefreshAsync();
        // A timeline range copy (the newest copy) pastes into the score as the notes of the shown track.
        return Clipboard.TryGetRange() is { } range ? _score.Editor.ClipFromRange(range) : Clipboard.TryGetClip(out _);
    }
}
