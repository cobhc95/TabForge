using TabForge.Documents;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Controllers;

/// <summary>The view parts a bar command redraws; the values equal the window's own refresh flags.</summary>
[Flags]
internal enum EditViews
{
    None = 0, Score = 1, Repaint = 2, Arrangement = 4, TimelineGeometry = 8,
    Palette = 16, Status = 32, Instrument = 64, Markers = 128,
}

/// <summary>What the bar commands need from their window.</summary>
internal interface IBarCommandHost
{
    /// <summary>The song the window shows now.</summary>
    DocumentSession Document { get; }
    EditingSettings Editing { get; }
    /// <summary>The bar under the edit cursor on the selected track, or null.</summary>
    MeasureModel? CurrentBar { get; }
    int SelectedBar { get; }
    int SelectedString { get; }
    int SelectedTrackIndex { get; }
    bool IsSelecting { get; }
    /// <summary>The first and last bar of the shared selection when bars are selected, else null.</summary>
    (int Start, int End)? SelectedBars { get; }
    void SetPosition(int bar, int cell, int stringIndex);
    void MoveToBarStart(int bar);
    void WriteLikeBeatBefore(int bar);
    void Refresh(EditViews views);
    /// <summary>Timeline, score layout and title after a bar was deleted (no cursor follow).</summary>
    void RefreshAfterBarDelete();
    void SetStatus(string text);
    bool ConfirmDeleteBar(int barNumber);
    (int num, int denom, bool onlyThisBar)? AskTimeSignature(int num, int denom, string? selectedBars);
    (int signature, bool minor, bool onlyThisBar)? AskKeySignature(int signature, bool minor, string? selectedBars);
    string? AskDirections(string current, int ending, out int selectedEnding);
    string? AskText(string title, string label, string initial);
}

// Owns: the bar commands of the Bar menu and the tools palette (insert, append and delete a bar, time and key signature, clef, triplet feel, directions, double bar, repeat-bar marks, section name) as edits through DocumentEdits.
// Does not own: the bar model work (ArrangementController, BarSignatures) or the dialogs (the host).
// Needs from its host: IBarCommandHost; every edit takes the host's Document.
// Tests: TestBarCommandFlow.
internal sealed class BarCommandFlow
{
    private readonly IBarCommandHost _host;
    private readonly ArrangementController _arrangement;

    public BarCommandFlow(IBarCommandHost host, ArrangementController arrangement)
    {
        _host = host;
        _arrangement = arrangement;
    }

    private DocumentSession Doc => _host.Document;
    private int MaxMeasures => BarRangeEditor.MaxMeasures(Doc.Project);

    public void InsertBar()
    {
        if (_host.IsSelecting) { _host.SetStatus("Insert bar works at the cursor; clear the selection first"); return; }   // as GP5 (quiet l6 i01, i03, i04)
        var at = Math.Clamp(_host.SelectedBar, 0, MaxMeasures);
        _arrangement.InsertBar(Doc, at, _host.SelectedBar, moveMarkers: false, fillRests: _host.Editing.FillBarsWithRests);
        _host.Refresh(EditViews.Score | EditViews.Arrangement);
        _host.MoveToBarStart(at);
        _host.WriteLikeBeatBefore(at);   // beat 1 of the new bar, the length of the beat before it (GP5)
        _host.SetStatus($"Inserted bar {at + 1}");
    }

    /// <summary>Adds an empty bar after the last one (the standard "Add bar"), keeping the cursor where it is.</summary>
    public void AppendBar()
    {
        var at = MaxMeasures;
        _arrangement.InsertBar(Doc, at, Math.Max(0, at - 1), moveMarkers: false, fillRests: _host.Editing.FillBarsWithRests);
        _host.Refresh(EditViews.Score | EditViews.Arrangement);
        _host.SetStatus($"Added bar {at + 1} at the end");
    }

    public void DeleteBar()
    {
        if (MaxMeasures <= 1) { _host.SetStatus("Cannot delete the last bar"); return; }
        if (_host.Editing.ConfirmDeleteBar && !_host.ConfirmDeleteBar(_host.SelectedBar + 1)) return;
        _arrangement.DeleteBar(Doc, _host.SelectedBar, -1, allTracks: true, moveMarkers: false);
        _host.SetPosition(Math.Max(0, _host.SelectedBar - 1), 0, _host.SelectedString);
        _host.RefreshAfterBarDelete();
        _host.SetStatus("Deleted bar");
    }

    public void SetTimeSignature()
    {
        var project = Doc.Project;
        var current = _host.CurrentBar;
        var range = SelectedBarRange();
        var r = _host.AskTimeSignature(current?.TimeSigNum ?? project.TimeSignatureNumerator,
            current?.TimeSigDenom ?? project.TimeSignatureDenominator, range is { } sel ? $"bars {sel.First + 1}-{sel.Last + 1}" : null);
        if (r is null) return;
        var first = range?.First ?? _host.SelectedBar;
        var last = first;
        DocumentEdits.Run(Doc, p =>
        {
            last = range is { } span
                ? BarSignatures.SetTimeRange(p, span.First, span.Last, r.Value.num, r.Value.denom)
                : BarSignatures.SetTime(p, first, r.Value.num, r.Value.denom, !r.Value.onlyThisBar);
            return true;
        });
        _host.Refresh(EditViews.Score | EditViews.TimelineGeometry | EditViews.Palette | EditViews.Status);
        _host.SetStatus($"Time signature {r.Value.num}/{r.Value.denom} {SignatureSpan(first, last)}");
    }

    public void SetKeySignature()
    {
        var project = Doc.Project;
        var current = _host.CurrentBar;
        var range = SelectedBarRange();
        var r = _host.AskKeySignature(current?.KeySignature ?? project.KeySignature,
            current?.KeySignatureMinor ?? project.KeySignatureMinor, range is { } sel ? $"bars {sel.First + 1}-{sel.Last + 1}" : null);
        if (r is null) return;
        var first = range?.First ?? _host.SelectedBar;
        var last = first;
        DocumentEdits.Run(Doc, p =>
        {
            last = range is { } span
                ? BarSignatures.SetKeyRange(p, span.First, span.Last, r.Value.signature, r.Value.minor)
                : BarSignatures.SetKey(p, first, r.Value.signature, r.Value.minor, !r.Value.onlyThisBar);
            return true;
        });
        _host.Refresh(EditViews.Score | EditViews.Palette | EditViews.Status);
        _host.SetStatus($"Key signature changed {SignatureSpan(first, last)}");
    }

    /// <summary>The selected bars when the selection spans more than one bar (a signature change then applies to exactly those bars).</summary>
    public (int First, int Last)? SelectedBarRange()
    {
        if (_host.SelectedBars is not { } bars) return null;
        var count = MaxMeasures;
        if (count == 0) return null;
        var (first, last) = (Math.Clamp(Math.Min(bars.Start, bars.End), 0, count - 1), Math.Clamp(Math.Max(bars.Start, bars.End), 0, count - 1));
        return last > first ? (first, last) : null;
    }

    /// <summary>"for bar 5", "from bar 5 to bar 9" or "from bar 5 to the end" (the bars a signature change reached).</summary>
    public string SignatureSpan(int first, int last) =>
        last <= first ? $"for bar {first + 1}" : last >= MaxMeasures - 1 ? $"from bar {first + 1} to the end" : $"from bar {first + 1} to bar {last + 1}";

    public void CycleClef()
    {
        if (_host.CurrentBar is null) return;
        var clef = "";
        if (!DocumentEdits.Run(Doc, p => _arrangement.TryCycleClef(p, _host.SelectedTrackIndex, _host.SelectedBar, out clef)).Changed) return;
        _host.Refresh(EditViews.Score);
        _host.SetStatus($"Clef {clef}");
    }

    public void ToggleTripletFeel()
    {
        if (_host.CurrentBar is null) return;
        var on = false;
        if (!DocumentEdits.Run(Doc, p => _arrangement.TryToggleTripletFeel(p, _host.SelectedTrackIndex, _host.SelectedBar, out on)).Changed) return;
        _host.Refresh(EditViews.None);
        _host.SetStatus(on ? "Triplet feel on" : "Triplet feel off");
    }

    public void EditDirections()
    {
        if (_host.CurrentBar is not { } bar) return;
        var text = _host.AskDirections(bar.Directions, bar.AlternateEnding, out var ending);
        if (text is null) return;
        DocumentEdits.Run(Doc, p => _arrangement.TrySetDirections(p, _host.SelectedBar, text, ending));
        _host.Refresh(EditViews.Score | EditViews.Palette);
    }

    public void ToggleDoubleBar()
    {
        if (_host.CurrentBar is null) return;
        if (!DocumentEdits.Run(Doc, p => _arrangement.TryToggleDoubleBar(p, _host.SelectedTrackIndex, _host.SelectedBar, out _)).Changed) return;
        _host.Refresh(EditViews.Score | EditViews.Palette);
    }

    /// <summary>Toggles the one-bar (<paramref name="barCount"/> 1) or two-bar (2) repeat mark on the current bar.</summary>
    public void ToggleSimile(int barCount)
    {
        if (_host.CurrentBar is null) return;
        if (!DocumentEdits.Run(Doc, p =>
            {
                var bar = p.Tracks[_host.SelectedTrackIndex].Measures[_host.SelectedBar];
                return _arrangement.TrySetSimile(p, _host.SelectedBar, barCount, !(barCount == 1 ? bar.SimileOneBar : bar.SimileTwoBar));
            }).Changed) return;
        _host.Refresh(EditViews.Score);
    }

    public void RenameSection()
    {
        if (_host.CurrentBar is not { } bar) return;
        var text = _host.AskText("Section", "Section name:", bar.SectionName);
        if (text is null) return;
        DocumentEdits.Run(Doc, p => _arrangement.TrySetSectionName(p, _host.SelectedBar, text));
        _host.Refresh(EditViews.Score | EditViews.Arrangement);
    }
}
