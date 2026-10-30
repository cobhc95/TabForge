using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Menus;
using TabForge.Presenters;

namespace TabForge.Avalonia.Views;

// Owns: the piano roll's right-click menu: the rows (Core PianoRollMenus) for the note or empty grid under the pointer, Clear loop on top inside the
//       loop, and running them: the piano roll's own rows here (the Velocity and Transpose values asked for first), Clear loop and Show in score
//       mode through the window's commands.
// Does not own: the rows (PianoRollMenus), the menu items (ContextMenuBuilder), the edits (PianoRollPresenter).
// Tests: TabForge.Avalonia.Tests.PianoRollViewTests.
public sealed partial class PianoRollView : IMenuCommandRunner
{
    private void AttachMenu() =>
        Surface.ContextRequested += (_, e) =>
        {
            if (Surface.SuppressMenu) { Surface.SuppressMenu = false; e.Handled = true; return; }   // the right button drew a box: no menu
            var rows = MenuRows().ToList();
            if (rows.Count == 0) return;
            ContextMenuBuilder.Show(Surface, rows, this);
            MenusOpened++;
            e.Handled = true;
        };

    /// <summary>How many right-click menus opened (a test reads it).</summary>
    internal int MenusOpened { get; private set; }

    /// <summary>The menu rows for the last right-press: the ruler's rows on the ruler, else the note's (the note it selected) or empty grid's.</summary>
    internal IEnumerable<MenuSpec> MenuRows()
    {
        var inside = LoopSpan() is var (from, to) && Surface.MenuBeat >= from && Surface.MenuBeat < to;
        var state = new PianoRollMenuState(Surface.MenuNote >= 0, Presenter.Clipboard.Count > 0, inside, Presenter.Notes.Count > 0, _host.Editing().PianoRollColours,
            SectionAt(Surface.MenuBeat) is not null, _host.Editing().PianoRollPlaybackNeedle);
        if (Surface.MenuInKeys) return new List<MenuSpec>();   // a right-click on the key strip selects; it never opens a menu
        return Surface.MenuInRuler ? PianoRollMenus.RulerRows(state) : PianoRollMenus.Rows(state);
    }

    /// <summary>Inverts the note selection (the Edit menu and Ctrl+I); true always (it selects all with nothing selected).</summary>
    internal bool RunInvert()
    {
        Presenter.InvertSelection();
        AfterSelection();
        return true;
    }

    /// <summary>The Edit menu's Copy, Cut, Paste (at the playhead), Paste special (the same paste) and Select all (null) on the piano roll's notes.</summary>
    internal bool RunEdit(TabForge.Services.EditVerb? verb)
    {
        switch (verb)
        {
            case null: Presenter.SelectAll(); AfterSelection(); return true;
            case TabForge.Services.EditVerb.Copy: Presenter.Copy(); return true;
            case TabForge.Services.EditVerb.Cut: Surface.Run(Presenter.CutAsync()); return true;
            case TabForge.Services.EditVerb.Paste or TabForge.Services.EditVerb.PasteSpecial: Surface.Run(Presenter.PasteAsync(PlayheadBeat() ?? 0)); return true;
            case TabForge.Services.EditVerb.Delete: Surface.Run(Presenter.DeleteAsync()); return true;
            case TabForge.Services.EditVerb.Duplicate: Surface.Run(Presenter.DuplicateAsync()); return true;
            default: return false;
        }
    }

    private static readonly IReadOnlySet<string> WindowRows = new HashSet<string>(StringComparer.Ordinal) { PianoRollMenus.ClearLoop, PianoRollMenus.ShowInScoreId };

    public bool Handles(string commandId) => WindowRows.Contains(commandId) ? _host.Commands.Handles(commandId) : commandId.StartsWith("PianoRoll.", StringComparison.Ordinal);

    public void Run(string commandId, string? argument = null)
    {
        if (WindowRows.Contains(commandId)) { _host.Commands.Run(commandId, argument); Surface.InvalidateVisual(); return; }
        switch (commandId)
        {
            case PianoRollMenus.Cut: Surface.Run(Presenter.CutAsync()); break;
            case PianoRollMenus.Copy: Presenter.Copy(); break;
            case PianoRollMenus.Paste: Surface.Run(Presenter.PasteAsync(PlayheadBeat() ?? Surface.MenuBeat)); break;
            case PianoRollMenus.Delete: Surface.Run(Presenter.DeleteAsync()); break;
            case PianoRollMenus.Duplicate: Surface.Run(Presenter.DuplicateAsync()); break;
            case PianoRollMenus.Quantize: Quantize(); break;
            case PianoRollMenus.QuantizeSettings: OpenQuantizeOptions(); break;
            case PianoRollMenus.Mute: Surface.Run(Presenter.ToggleMuteAsync()); break;
            case PianoRollMenus.TransposeUp: Surface.Run(Presenter.TransposeAsync(1)); break;
            case PianoRollMenus.TransposeDown: Surface.Run(Presenter.TransposeAsync(-1)); break;
            case PianoRollMenus.OctaveUp: Surface.Run(Presenter.TransposeAsync(12)); break;
            case PianoRollMenus.OctaveDown: Surface.Run(Presenter.TransposeAsync(-12)); break;
            case PianoRollMenus.TransposeBy: AskTranspose(); break;
            case PianoRollMenus.VelocitySet: Ask("Set velocity", "Velocity (1 to 127):", AverageVelocity(), v => Presenter.SetVelocityAsync((int)v)); break;
            case PianoRollMenus.VelocityScale: Ask("Scale velocity", "Scale by percent:", "100", v => Presenter.ScaleVelocityAsync(v)); break;
            case PianoRollMenus.VelocityRamp: AskRamp(); break;
            case PianoRollMenus.SelectAll: Presenter.SelectAll(); AfterSelection(); break;
            case PianoRollMenus.InvertSelection: Presenter.InvertSelection(); AfterSelection(); break;
            case PianoRollMenus.SelectPitch: Presenter.SelectPitch(Surface.MenuNote >= 0 ? Surface.MenuNote : Presenter.Selected.FirstOrDefault(-1)); AfterSelection(); break;
            case PianoRollMenus.Legato: Surface.Run(Presenter.LegatoAsync()); break;
            case PianoRollMenus.Glue: Surface.Run(Presenter.GlueAsync()); break;
            case PianoRollMenus.Randomise: Ask("Randomise velocity", "Move each velocity by up to (±):", "10", v => Presenter.RandomiseVelocityAsync((int)v)); break;
            case PianoRollMenus.Humanise: Ask("Humanise timing", "Move each start by up to (± ticks, 480 to a beat):", "15", v => Presenter.HumaniseAsync(v / 480)); break;
            case PianoRollMenus.Colours when argument is not null: _host.Editing().PianoRollColours = argument; _host.SaveSettings(); RefreshDerived(); Surface.InvalidateVisual(); break;
            case PianoRollMenus.PlaybackNeedle: _host.Editing().PianoRollPlaybackNeedle = !_host.Editing().PianoRollPlaybackNeedle; _host.SaveSettings(); RefreshNeedle(); break;
            case PianoRollMenus.MovePlayhead: Seek(Surface.MenuBeat); break;
            case PianoRollMenus.LoopBar: { var bar = Presenter.Map.BarAtBeat(Surface.MenuBeat); MakeLoop(bar.StartBeat, bar.EndBeat, false); break; }
            case PianoRollMenus.LoopSection when SectionAt(Surface.MenuBeat) is var (start, end): _host.MakeLoop(start, 0, end, -1, false); Surface.InvalidateVisual(); break;
            case LaneCommand when argument == AnyCc: AskAnyCc(); break;
            case LaneCommand when int.TryParse(argument, NumberStyles.Integer, CultureInfo.InvariantCulture, out var lane): ShowLane(lane); break;
        }
    }

    /// <summary>Asks for a CC number, then shows that lane (nothing when cancelled or not a number from 0 to 127).</summary>
    private void AskAnyCc() => UiTasks.Forget(AskAnyCcThen(), "Any CC", _host.Report);

    private async Task AskAnyCcThen()
    {
        var text = await _host.AskText("Any CC", "CC number (0 to 127):", "2");
        if (text is null || !int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var cc)) return;
        ShowLane(Math.Clamp(cc, 0, 127));
    }

    private void AfterSelection()
    {
        Surface.InvalidateVisual();
        RefreshInfo();
    }

    private string AverageVelocity() =>
        Presenter.Selected.Count == 0 ? "100" : ((int)Math.Round(Presenter.Selected.Average(i => Presenter.Notes[i].Velocity))).ToString(CultureInfo.InvariantCulture);

    /// <summary>T with notes selected (and the Move notes menu row): the Move notes popup with the selection's count and chips (tests answer it directly).</summary>
    internal Func<PianoRollTransposeDialog, Task<MoveNotesAnswer?>>? TransposePrompt { get; set; }

    internal void AskTranspose()
    {
        var notes = Presenter.Selected.Where(i => i < Presenter.Notes.Count).Select(i => Presenter.Notes[i]).ToList();
        if (notes.Count == 0) return;
        var label = PianoRollScale.DisplayLabel(EffectiveScaleHighlight(), false);
        var model = new PianoRollTransposeDialog(notes.Count, notes.Select(n => n.Pitch).ToList(), 1, Presenter.ScaleSteps, Presenter.Scale, Presenter.ScaleLock,
            Presenter.Scale is null ? null : label);
        UiTasks.Forget(AskTransposeThen(model), "Move notes", _host.Report);
    }

    private async Task AskTransposeThen(PianoRollTransposeDialog model)
    {
        var answer = TransposePrompt is not null ? await TransposePrompt(model) : await PianoRollTransposePopup.AskAsync(this, model, StoreMoveBy);
        if (answer is not { } move) return;
        if (move.ScaleSteps != model.ScaleSteps || Presenter.ScaleSteps != move.ScaleSteps) StoreMoveBy(move.ScaleSteps);
        Surface.Run(Presenter.TransposeAsync(move.Delta, move.Snap, move.ScaleSteps));
    }

    /// <summary>The Move notes popup's Half steps / Stay in the key choice IS the Move by setting (also used by the Up / Down transpose keys).</summary>
    private void StoreMoveBy(bool stayInKey)
    {
        _host.Editing().PianoRollTransposeMode = stayInKey ? PianoRollScaleLock.TransposeScaleSteps : PianoRollScaleLock.TransposeSemitones;
        _host.SaveSettings();
        RefreshDerived();
    }

    /// <summary>Asks for a number, then runs the edit with it (nothing when cancelled or not a number).</summary>
    private void Ask(string title, string label, string initial, Func<double, Task<bool>> edit) =>
        UiTasks.Forget(AskThen(title, label, initial, edit), title, _host.Report);

    private async Task AskThen(string title, string label, string initial, Func<double, Task<bool>> edit)
    {
        var text = await _host.AskText(title, label, initial);
        if (text is null || !double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) return;
        Surface.Run(edit(value));
    }

    private void AskRamp() => UiTasks.Forget(AskRampThen(), "Velocity ramp", _host.Report);

    private async Task AskRampThen()
    {
        var text = await _host.AskText("Velocity ramp", "From the first to the last selected note (two velocities, such as 40 120):", "40 120");
        var parts = text?.Split(new[] { ' ', ',', '-', '>' }, StringSplitOptions.RemoveEmptyEntries) ?? Array.Empty<string>();
        if (parts.Length < 2 || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var first)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var last)) return;
        Surface.Run(Presenter.RampVelocityAsync(first, last));
    }
}
