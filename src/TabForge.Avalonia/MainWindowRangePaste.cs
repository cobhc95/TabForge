using System;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the two commands of a copied timeline range pasted onto the timeline: Range.Paste (Overwrite at the insert point, no question) and Range.PasteSpecial (the Paste Special window, its choices
//   remembered in the settings when asked), the insert point (the start of the selected bars, else the playhead bar) and first track, ONE undo step through RangePaste, selecting the pasted bars and the
//   one top-bar notice. With no copied range they run the score paste instead (the clipboard may hold score notes).
// Does not own: the paste rules (RangePaste, PasteMapper), the clipboard (ClipboardService), the window (PasteSpecialWindow) or the key routing (EditRouting, MainWindowKeyboard).
// Tests: TabForge.Avalonia.Tests.TimelinePasteTests.
internal static class MainWindowRangePaste
{
    /// <summary>Asks the Paste Special window (an injected fake in tests); null = Cancel.</summary>
    public delegate Task<PasteSpecialOptions?> PastePrompt(RangePasteRequest request);

    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, MainWindowClipboard flow, Action<string> notice,
        PastePrompt ask, Func<EditingSettings> editing, Action saveSettings, Func<int> playheadBar)
    {
        // A focused track header also applies: the range pastes starting at that track (EditRouting sends it here only when a range was copied).
        bool HasRange() => SelectionBars.For(panes.Selection.Model, songs.Active.Project) is not null || panes.Selection.Model is { Focus: FocusTarget.TrackRow, TrackRowCount: > 0 };
        void Run(bool special) => Platform.UiTasks.Forget(RunAsync(special), special ? "Paste special" : "Paste", notice);
        async Task RunAsync(bool special)
        {
            var model = panes.Selection.Model;
            int? headerTrack = SelectionBars.For(model, songs.Active.Project) is null && model.Focus == FocusTarget.TrackRow && model.Rows.Count > 0
                ? songs.Active.Project.Tracks.FindIndex(t => t.Id == model.Rows[0]) : null;
            if (await flow.RangeToPasteAsync() is { } snapshot)
                await PasteAsync(songs.Active, model, snapshot, playheadBar(), special, ask, editing, saveSettings, notice, headerTrack is >= 0 ? headerTrack : null);
            else if (special) await flow.PasteSpecialAsync();
            else await flow.PasteAsync();
        }
        commands.AddWhen(EditRouting.RangePasteId, () => Run(false), HasRange);
        commands.AddWhen(EditRouting.RangePasteSpecialId, () => Run(true), HasRange);
    }

    /// <summary>
    /// Pastes <paramref name="snapshot"/> at the insert point as one undo step: Overwrite with the default options, or (<paramref name="special"/>) the choices of the Paste Special window, which starts from
    /// the remembered ones. Selects the pasted bars and reports one notice. Returns true when the song changed; false on Cancel or when nothing could be pasted.
    /// </summary>
    internal static async Task<bool> PasteAsync(IEditableDocument document, SelectionModel model, RangeSnapshot snapshot, int playheadBar, bool special,
        PastePrompt ask, Func<EditingSettings> editing, Action saveSettings, Action<string> notice, int? headerTrack = null)
    {
        var settings = editing();
        var options = new RangePasteOptions();
        if (special)
        {
            var initial = settings.RememberPasteSpecial ? settings.PasteSpecialRange.Normalized() : new RangePasteOptions();
            var request = new RangePasteRequest(initial, settings.RememberPasteSpecial, snapshot.Tracks.Any(t => t.Kind != TrackKind.Audio), snapshot.Tracks.Any(t => t.Clips.Count > 0));
            if (await ask(request) is not { Range: { } chosen } answer) return false;
            options = chosen.Normalized();
            settings.RememberPasteSpecial = answer.Remember;
            if (answer.Remember) settings.PasteSpecialRange = options;
            saveSettings();
        }
        var tracks = document.Project.Tracks.Count;
        if (tracks == 0) return false;
        // At the start of SelectionBars (the loop on every track, else the selected clips from their first track), else at the playhead.
        var span = SelectionBars.For(model, document.Project);
        var at = span?.Start ?? Math.Max(0, playheadBar);
        var track = Math.Clamp(span is { Tracks: not null } ? span.FirstTrack : headerTrack ?? 0, 0, tracks - 1);
        RangePasteResult? result = null;
        DocumentEdits.Run(document, project => (result = RangePaste.Apply(project, snapshot, at, track, options)).Changed);
        if (result is not { Changed: true })
        {
            if (result?.Notice is { } why) notice(why);
            return false;
        }
        var bars = result.LastBar - result.FirstBar + 1;
        notice($"Pasted {bars} {(bars == 1 ? "bar" : "bars")} at bar {result.FirstBar + 1}" + (result.Notice is { } extra ? "; " + extra : ""));
        return true;
    }
}
