using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Avalonia.Platform;
using TabForge.ScoreEditing;
using TabForge.Services;
using TabForge.Views.EffectEditors;
using TabForge.Menus;

namespace TabForge.Avalonia;

// Owns: which Edit, Note and Bar menu commands the Avalonia window runs, and wiring them to the score editor of the shown song: Undo, Redo
//   and the commands ScoreEditCommands.TryRunNoteCommand, the note values and the bar insert, delete and duplicate run (the same calls the score's own keys make),
//   and Repeat selection after its count prompt (RepeatSelectionPromptPresenter, Views.TextPromptWindow).
// Does not own: the edit rules and undo grouping (Core ScorePreviewEditor, ScoreEditCommands, DocumentEdits), the shown song
// (MainWindowDocumentPanes). Commands needing a clipboard stay unhandled. Bar.Delete asks first when the confirm setting is on.
// Tests: TabForge.Avalonia.Tests.MainWindowDocumentsTests.
/// <summary>Registers the editing commands. Built once per window.</summary>
public static class MainWindowEdit
{
    /// <summary>The command ids ScorePreviewEditor.RunCommand handles (a test runs each, so the list cannot drift from ScoreEditCommands).</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        CommandIds.Edit.Undo, CommandIds.Edit.Redo, "Edit.InsertBeat", "Edit.DeleteBeats", CommandIds.Note.RepeatBeat,
        "Note.Longer", "Note.Shorter", CommandIds.Note.Dot, CommandIds.Note.DoubleDot, CommandIds.Note.Triplet, CommandIds.Note.Tie, "Note.Rest",
        CommandIds.Note.Fermata, CommandIds.Note.Accent, CommandIds.Note.Staccato, "Note.Tenuto",
        CommandIds.Note.PitchUp, CommandIds.Note.PitchDown, CommandIds.Note.MoveStringUp, CommandIds.Note.MoveStringDown,
        CommandIds.Note.Dead, CommandIds.Note.Ghost, CommandIds.Note.PalmMute, CommandIds.Note.LetRing, CommandIds.Note.HammerPull, CommandIds.Note.Slide, CommandIds.Note.Vibrato, CommandIds.Note.FadeIn, "Note.FadeOut",
        CommandIds.Note.Bend, "Note.ShiftSlide", "Note.WideVibrato", CommandIds.Note.TremoloBar, CommandIds.Note.TremoloPicking,
        "Note.ArtificialHarmonic", CommandIds.Note.Tapping, CommandIds.Note.Slap, CommandIds.Note.Pop, "Note.WahOpen", "Note.WahClose",
        CommandIds.Note.BrushDown, CommandIds.Note.BrushUp, "Note.ArpeggioDown", "Note.ArpeggioUp",
        CommandIds.Bar.RepeatOpen, CommandIds.Bar.RepeatClose, CommandIds.Bar.Insert, CommandIds.Bar.Delete, CommandIds.Bar.Duplicate,
        CommandIds.Note.Whole, CommandIds.Note.Half, CommandIds.Note.Quarter, CommandIds.Note.Eighth, CommandIds.Note.Sixteenth, CommandIds.Note.ThirtySecond, CommandIds.Note.SixtyFourth,
        "Bar.DoubleBar", "Bar.RepeatOneBar", "Bar.RepeatTwoBars", CommandIds.Section.Previous, CommandIds.Section.Next,
    };

    /// <summary>The Structure, Rhythm and Layout tools the editor runs without a question, under their palette command ids (those with no menu row of their own).</summary>
    public static IEnumerable<string> ToolCommands => PaletteToolCatalog.EditorCommandIds.Except(Commands);

    /// <param name="owner">The window the repeat-count question opens over.</param>
    /// <param name="contextMenus">The host whose ClickedBar the timeline bar menu's insert rows run at.</param>
    /// <param name="confirmDeleteBar">Reads the "Confirm before deleting a bar" setting when Bar.Delete runs.</param>
    /// <param name="notice">Shows a status line (the refused Bar.Delete on the last bar).</param>
    /// <param name="showMarkerList">Brings the Sections pane into view (the Marker list tool); null when the window has none.</param>
    /// <param name="settings">The settings the effect editors read their own presets from; null keeps none.</param>
    /// <param name="seekPlayback">Seeks playback (and the playhead) to a bar after Section.Previous or Section.Next moved the cursor; null leaves playback alone.</param>
    /// <param name="saveSettings">Writes the settings after an effect preset is saved or deleted; null writes nothing.</param>
    /// <param name="afterInsert">Called after a bar or section insert landed (Bar.Insert, the timeline bar menu's insert rows, Section.Add): the shared insert refresh; null does nothing more.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window owner, ContextMenuHost contextMenus,
        Func<bool> confirmDeleteBar, Action<string> notice, Action? showMarkerList = null, Func<AppSettings>? settings = null, Action? saveSettings = null, Action<int>? seekPlayback = null, Action? afterInsert = null)
    {
        score.Editor.ConfirmDeleteBarSetting = confirmDeleteBar;
        score.Editor.Notice = notice;
        score.Editor.AskDeleteBar = bar => _ = AskAndDeleteBar(score, owner, bar);
        var ornaments = new OrnamentEditorFlow(score, window => window.ShowDialog<EffectValuesAnswer?>(owner), notice, settings, saveSettings);
        var vibrato = new VibratoEditorFlow(score, window => window.ShowDialog<EffectValuesAnswer?>(owner), notice, settings);
        var ornamentRows = new Dictionary<string, Action>(StringComparer.Ordinal)
        {   // the trill, grace and harmonic palette buttons and hotkeys open the same editors as the Note menu rows
            ["Note.Trill"] = () => _ = ornaments.OpenTrill(),
            ["Note.Grace"] = () => _ = ornaments.OpenGrace(),
            ["Note.Harmonic"] = () => _ = ornaments.OpenOrRemoveHarmonic(),
            [CommandIds.Note.Vibrato] = () => _ = vibrato.OpenOrToggle(),
        };
        foreach (var id in Commands.Concat(ToolCommands).Concat(ornamentRows.Keys).Distinct())
        {
            var commandId = id;
            if (seekPlayback is not null && commandId is CommandIds.Section.Previous or CommandIds.Section.Next)
            {   // the transport and keyboard section jumps seek playback to the section start too, without starting or pausing it
                commands.Add(commandId, () => { if (score.Run(commandId)) seekPlayback(score.Editor.SelectedMeasure); });
                continue;
            }
            if (commandId == CommandIds.Bar.Insert) { commands.Add(commandId, () => { if (score.Run(commandId)) afterInsert?.Invoke(); }); continue; }
            commands.Add(commandId, ornamentRows.TryGetValue(commandId, out var open) ? open : () => score.Run(commandId));
        }
        commands.Add("Note.TrillEditor", () => _ = ornaments.OpenTrill());
        commands.Add("Note.VibratoSettings", () => _ = vibrato.Open());
        commands.Add("Note.HarmonicEditor", () => _ = ornaments.OpenHarmonic());
        MainWindowNoteDialogs.Register(commands, score, owner, () => _ = ornaments.OpenGrace());
        MainWindowBarTools.Register(commands, score, owner, showMarkerList, afterInsert);
        MainWindowPaletteTools.Register(commands, score);
        MainWindowBarMenu.Register(commands, score, owner);
        commands.Add("Edit.RepeatSelection", () => _ = AskAndRepeat(score, owner));
        var readSettings = settings ?? SettingsDefaults.Fallback;
        var writeSettings = saveSettings ?? (() => { });
        commands.Add("Note.BendEditor", () => _ = EffectCurveFlow.Open(score, owner, new AvaloniaDialogHost(), readSettings, writeSettings, EffectEditorKind.Bend));
        commands.Add("Note.TremoloBarEditor", () => _ = EffectCurveFlow.Open(score, owner, new AvaloniaDialogHost(), readSettings, writeSettings, EffectEditorKind.TremoloBar));
        commands.Add(ContextMenuRows.InsertBarBeforeId, () => { if (score.InsertBarAt(contextMenus.Target.Bar)) afterInsert?.Invoke(); });
        commands.Add(ContextMenuRows.InsertBarAfterId, () => { if (score.InsertBarAt(contextMenus.Target.Bar + 1)) afterInsert?.Invoke(); });
    }

    // The question (ConfirmDeleteBar): Yes deletes the bar from every track, No (the default) leaves the song as it is.
    private static async Task AskAndDeleteBar(ScorePreviewView score, Window owner, int bar)
    {
        var answer = await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, ScoreBarCommands.ConfirmDeleteText(bar), "Delete bar",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No);
        if (answer == MessageBoxAnswer.Yes) score.Edit(editor => editor.DeleteBarConfirmed(bar));
    }

    private static async System.Threading.Tasks.Task AskAndRepeat(ScorePreviewView score, Window owner)
    {
        var asked = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(RepeatSelectionPromptPresenter.Title, RepeatSelectionPromptPresenter.Label, RepeatSelectionPromptPresenter.Default));
        if (RepeatSelectionPromptPresenter.Times(asked) is { } times) score.Repeat(times);
    }
}
