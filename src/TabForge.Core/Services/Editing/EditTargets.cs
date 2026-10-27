using TabForge.Models;

namespace TabForge.Services;

// Owns: the shown document's effective edit focus for the edit keys and menus: the last-clicked focus run through the user's edit target setting (EditTargetRule).
// Does not own: the rule (EditTargetRule), the focus (SelectionModel), the highlight (BarHighlight) or the settings store.
// Tests: no dedicated test class.
public sealed class EditTargets
{
    private readonly Func<SelectionModel> _selection;
    private readonly Func<BarHighlight> _highlight;
    private readonly Func<EditTargetMode> _mode;

    /// <param name="selection">The shown document's selection.</param>
    /// <param name="highlight">The shown document's highlight.</param>
    /// <param name="mode">The edit target setting as it is now.</param>
    public EditTargets(Func<SelectionModel> selection, Func<BarHighlight> highlight, Func<EditTargetMode> mode)
    {
        _selection = selection;
        _highlight = highlight;
        _mode = mode;
    }

    public SelectionModel Selection => _selection();

    public BarHighlight Highlight => _highlight();

    /// <summary>What Copy, Cut, Delete and Paste act on now.</summary>
    public FocusTarget Focus => EditTargetRule.Effective(_mode(), Selection.Focus, Selection, Highlight);
}
