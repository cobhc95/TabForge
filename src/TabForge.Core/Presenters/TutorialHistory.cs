namespace TabForge.Presenters;

/// <summary>
/// The tutorial reader's Back and Forward history: each visit is a chapter and an optional heading, and a step moves along the visits.
/// Owns: the visit rules (a repeat of the shown entry adds nothing, a visit after a step back drops the entries ahead) and the step bounds.
/// Does not own: the navigation itself (Views/TutorialWindow.xaml.cs) or the Back and Forward enabled states (TutorialPresenter.NavButtons).
/// Tests: no dedicated test class.
/// </summary>
public sealed class TutorialHistory
{
    private readonly List<(string ChapterId, string? HeadingId)> _entries = new();

    /// <summary>The number of entries.</summary>
    public int Count => _entries.Count;

    /// <summary>The position of the shown entry, or -1 when there is none.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>Forgets every entry.</summary>
    public void Clear()
    {
        _entries.Clear();
        Index = -1;
    }

    /// <summary>Records a visit. A visit equal to the shown entry adds nothing; any other visit drops the entries ahead of the position first.</summary>
    public void Visit(string chapterId, string? headingId)
    {
        if (Index >= 0 && _entries[Index] == (chapterId, headingId)) return;
        if (Index < _entries.Count - 1) _entries.RemoveRange(Index + 1, _entries.Count - Index - 1);
        _entries.Add((chapterId, headingId));
        Index = _entries.Count - 1;
    }

    /// <summary>Moves the position by <paramref name="delta"/> and returns the entry there; null, and no move, when that is outside the history.</summary>
    public (string ChapterId, string? HeadingId)? Step(int delta)
    {
        var target = Index + delta;
        if (target < 0 || target >= _entries.Count) return null;
        Index = target;
        return _entries[target];
    }
}
