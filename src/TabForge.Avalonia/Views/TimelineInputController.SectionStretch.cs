using System;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: stretching a section by dragging its right edge past the next section's start: the bump (12 px of resistance), the whole bars under the pointer beyond it while the edge is dragged (the preview is
//   TimelineView.SectionStretchBars, no model edit until the release), and the release: the remembered choice (EditingSettings.SectionStretch) or the prompt, then the one edit (SectionStretch.Apply) as one undo
//   step. Cancel on the prompt, Escape during the drag and "Stop at the next section" stretch nothing.
// Does not own: the edge drag itself (TimelineInputController.SectionResize), the bars rule and the edits (SectionStretch in Core), the prompt window (SectionStretchPromptWindow) or the preview drawing (TimelineView.SectionStretch).
// Tests: TabForge.Avalonia.Tests.SectionStretchTests.
public sealed partial class TimelineInputController
{
    private SectionStretchPlan? _stretchPlan;
    private SectionStretchBackPlan? _stretchBackPlan;
    private int _stretchBars;
    private bool _stretchAsking;

    /// <summary>The "when stretching a section past the next one" setting (EditingSettings.SectionStretch), read at each release; null asks.</summary>
    public Func<string>? SectionStretchSetting { get; set; }

    /// <summary>Stores the choice a ticked "Remember my choice" gives in the settings.</summary>
    public Action<string>? SectionStretchRemember { get; set; }

    /// <summary>Asks how to stretch (null answer = Cancel, nothing changes). Without it a stretch only closes the gap up to the next section.</summary>
    public Func<SectionStretchQuestion, Task<SectionStretchAnswer?>>? SectionStretchPrompt { get; set; }

    /// <summary>The "when stretching a section backwards" setting (EditingSettings.SectionStretchBack), read at each release; null asks.</summary>
    public Func<string>? SectionStretchBackSetting { get; set; }

    /// <summary>Stores the backward choice a ticked "Remember my choice" gives in the settings.</summary>
    public Action<string>? SectionStretchBackRemember { get; set; }

    /// <summary>Asks how to stretch a section back (null answer = Cancel). Without it a backward stretch only closes the gap up to the previous section.</summary>
    public Func<SectionStretchBackQuestion, Task<SectionStretchBackAnswer?>>? SectionStretchBackPrompt { get; set; }

    // The whole bars the pointer asks for past the next section's start plus the bump: each added bar is as wide as the section's last bar.
    private int StretchBarsAt(double x)
    {
        if (_stretchBackPlan is { } back)   // backwards: each added bar is as wide as the section's first bar
        {
            var boundary = _view.BarX(back.Boundary);
            return SectionStretchBack.BarsFor(x, boundary, _view.BarX(back.Start + 1) - _view.BarX(back.Start), SectionStretch.Room(_document.Project));
        }
        if (_stretchPlan is not { } plan) return 0;
        var edge = _view.BarX(plan.NextStart);
        return SectionStretch.BarsFor(x, edge, plan.NextStart > 0 ? edge - _view.BarX(plan.NextStart - 1) : 0, SectionStretch.Room(_document.Project));
    }

    // The release of an edge held past the next section by bars: the remembered choice applies at once, else the prompt opens over the still-shown preview. True when the prompt is open.
    private bool CommitStretch(MarkerModel marker, int bars)
    {
        var document = _document;
        if (SectionStretchChoice.Remembered(SectionStretchSetting?.Invoke()) is { } remembered) ApplyStretch(document, marker, remembered, bars);
        else if (SectionStretchPrompt is not { } ask || SectionStretch.PlanFor(document.Project, marker) is not { } plan) ApplyStretch(document, marker, SectionStretchMode.Stop, 0);
        else
        {
            UiTasks.Forget(AskStretch(document, marker, plan, bars, ask), "Stretching the section", _ => { _stretchAsking = false; ClearDrag(); });
            return true;
        }
        _view.SectionResizePreview = null;
        _view.SectionStretchBars = _stretchBars = 0;
        Repaint();
        return false;
    }

    private async Task AskStretch(IEditableDocument document, MarkerModel marker, SectionStretchPlan plan, int bars, Func<SectionStretchQuestion, Task<SectionStretchAnswer?>> ask)
    {
        try
        {
            var answer = await ask(SectionStretchPromptPresenter.Question(document.Project, plan, bars));
            if (!ReferenceEquals(document, _document) || !document.Project.Markers.Contains(marker) || answer is not { } picked) return;   // the tab changed under the prompt, or Cancel
            if (picked.Remember) SectionStretchRemember?.Invoke(SectionStretchChoice.Store(picked.Mode));
            ApplyStretch(document, marker, picked.Mode, bars);
        }
        finally
        {
            _stretchAsking = false;
            ClearDrag();
        }
    }

    // The one edit: the section's end reaches the next section, then the chosen option (one undo step); the engine hears the bar map when bars went in.
    private void ApplyStretch(IEditableDocument document, MarkerModel marker, SectionStretchMode mode, int bars)
    {
        int[]? map = null;
        var changed = DocumentEdits.Run(document, song => SectionStretch.Apply(song, marker, mode, bars, out map), label: "Stretch section").Changed;
        if (!changed) return;
        AfterEdit();
        if (map is { } moved) SectionMoved?.Invoke(document, moved);
    }

    // The release of a left edge held past the previous section by bars: as CommitStretch, backwards.
    private bool CommitStretchBack(MarkerModel marker, int bars)
    {
        var document = _document;
        if (SectionStretchBackChoice.Remembered(SectionStretchBackSetting?.Invoke()) is { } remembered) ApplyStretchBack(document, marker, remembered, bars);
        else if (SectionStretchBackPrompt is not { } ask || SectionStretchBack.PlanFor(document.Project, marker) is not { } plan) ApplyStretchBack(document, marker, SectionStretchBackMode.Stop, 0);
        else
        {
            UiTasks.Forget(AskStretchBack(document, marker, plan, bars, ask), "Stretching the section", _ => { _stretchAsking = false; ClearDrag(); });
            return true;
        }
        _view.SectionResizePreview = null;
        _view.SectionStretchBars = _stretchBars = 0;
        Repaint();
        return false;
    }

    private async Task AskStretchBack(IEditableDocument document, MarkerModel marker, SectionStretchBackPlan plan, int bars, Func<SectionStretchBackQuestion, Task<SectionStretchBackAnswer?>> ask)
    {
        try
        {
            var answer = await ask(SectionStretchBackPromptPresenter.Question(plan, bars));
            if (!ReferenceEquals(document, _document) || !document.Project.Markers.Contains(marker) || answer is not { } picked) return;
            if (picked.Remember) SectionStretchBackRemember?.Invoke(SectionStretchBackChoice.Store(picked.Mode));
            ApplyStretchBack(document, marker, picked.Mode, bars);
        }
        finally
        {
            _stretchAsking = false;
            ClearDrag();
        }
    }

    private void ApplyStretchBack(IEditableDocument document, MarkerModel marker, SectionStretchBackMode mode, int bars)
    {
        int[]? map = null;
        var changed = DocumentEdits.Run(document, song => SectionStretchBack.Apply(song, marker, mode, bars, out map), label: "Stretch section").Changed;
        if (!changed) return;
        AfterEdit();
        if (map is { } moved) SectionMoved?.Invoke(document, moved);
    }
}
