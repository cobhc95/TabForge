using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Controllers;

/// <summary>What the selection/loop controller needs from its window.</summary>
internal interface ISelectionLoopHost
{
    /// <summary>The song on show; the loop range lives on it.</summary>
    DocumentSession ActiveDocument { get; }
    bool LoopOn { get; }
    /// <summary>Turns looping on or off (re-derives the loop range and the engine loop).</summary>
    void SetLoopActive(bool loop);
    /// <summary>Redraws the area, loop and skip marks on the timeline and hands the skip ranges to the engine.</summary>
    void SyncAreaVisuals();
    /// <summary>Shows a new area: timeline loop range, engine loop range, loop highlight and the status text.</summary>
    void ShowArea(int start, int end, int startCell, int endCell);
    /// <summary>Shows the score selection (bar range, scope track) as the timeline highlight.</summary>
    void ShowScoreSelection(SelectionModel selection);
}

// Owns: the selected (loop) area of the active song (its bar/cell range and whether an area exists) and the
//   shared-selection to timeline/loop-area sync.
// Does not own: the selection data (SelectionModel), the loop on/off flag (DocumentSession), the views' drawing
//   or the engine (the host applies those).
// Tests: TestSelectionClipboardMatrix, TestSelectionModel.
/// <summary>Holds the selected (loop) area and applies the shared selection to the timeline.</summary>
public sealed class SelectionLoopController
{
    private readonly ISelectionLoopHost _host;

    internal SelectionLoopController(ISelectionLoopHost host) => _host = host;

    /// <summary>True while a selected area exists (it always equals the shared selection).</summary>
    public bool HasArea { get; private set; }

    public int StartBar { get => _host.ActiveDocument.LoopStartBar; private set => _host.ActiveDocument.LoopStartBar = value; }
    public int EndBar { get => _host.ActiveDocument.LoopEndBar; private set => _host.ActiveDocument.LoopEndBar = value; }
    public int StartCell { get => _host.ActiveDocument.LoopStartCell; private set => _host.ActiveDocument.LoopStartCell = value; }
    public int EndCell { get => _host.ActiveDocument.LoopEndCell; private set => _host.ActiveDocument.LoopEndCell = value; }

    public bool Contains(int bar) => HasArea && bar >= StartBar && bar <= EndBar;

    /// <summary>Sets the loop range without an area (the no-area loop: the whole song or the section).</summary>
    public void SetRange(int start, int end, int startCell, int endCell)
    {
        StartBar = start;
        EndBar = end;
        StartCell = startCell;
        EndCell = endCell;
    }

    /// <summary>Forgets the area (the range values stay as they were).</summary>
    public void Drop() => HasArea = false;

    /// <summary>The timeline's score-selection highlight and the selected (loop) area both show the model's range.</summary>
    public void ApplySelection(SelectionModel s, SelectionOrigin origin)
    {
        _host.ShowScoreSelection(s);
        if (s.HasRange)
        {
            ApplyArea(s.StartBar, s.EndBar, s.StartCell, s.EndCell);
            return;
        }
        if (!HasArea) return;
        HasArea = false;
        // Clearing the area while looping falls back to the no-area loop (the whole song), as Esc always did.
        // A tab switch only drops the area: the new song's loop is set up by the switch itself.
        if (_host.LoopOn && origin != SelectionOrigin.Document) _host.SetLoopActive(true);
        else _host.SyncAreaVisuals();
    }

    /// <summary>Shows the score selection as the timeline highlight (no area change).</summary>
    public void ShowScore(SelectionModel s) => _host.ShowScoreSelection(s);

    private void ApplyArea(int start, int end, int startCell, int endCell)
    {
        SetRange(start, end, Math.Max(0, startCell), endCell);
        HasArea = true;
        _host.ShowArea(start, end, StartCell, EndCell);
    }
}
