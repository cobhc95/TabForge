// Owns: the preset manage panel's rules without UI types: the plan of an inline rename (the name's error, a rename to the same name, the
//       other preset a rename would replace) and the moves of a run of chosen presets (BlockSteps).
// Does not own: the store and its files (IPresetStore), the list, the messages and questions (PresetManagePanel), or the undo.
// Tests: TestPresetManagePanel.
namespace TabForge.Presenters;

/// <summary>
/// What a typed rename does: <see cref="Error"/> refuses it; <see cref="Unchanged"/> means the same name; <see cref="ReplacesName"/> is
/// the other preset (any case) the new name would replace, which the user must confirm.
/// </summary>
public sealed record PresetRenamePlan(string Name, string? Error, bool Unchanged, string? ReplacesName);

public static class PresetManagePresenter
{
    /// <summary>Plans a rename of <paramref name="from"/> to the trimmed <paramref name="text"/> among <paramref name="names"/>.</summary>
    public static PresetRenamePlan PlanRename(string from, string text, IReadOnlyList<string> names)
    {
        var name = text.Trim();
        if (PresetRules.Error(name) is { } error) return new PresetRenamePlan(name, error, false, null);
        if (name == from) return new PresetRenamePlan(name, null, true, null);
        var clash = names.FirstOrDefault(n => Same(n, name) && !Same(n, from));
        return new PresetRenamePlan(name, null, false, clash);
    }

    /// <summary>
    /// The moves that shift the chosen names one row (delta) as a block: each moves past the row beside it when that row is not chosen,
    /// so a name at the edge, or behind a chosen one that cannot move, stays. Empty when nothing can move.
    /// </summary>
    public static List<(string Name, int Index)> BlockSteps(IReadOnlyList<string> order, IReadOnlyList<string> chosen, int delta)
    {
        var list = order.ToList();
        var set = new HashSet<string>(chosen, StringComparer.OrdinalIgnoreCase);
        var moving = list.Where(set.Contains).ToList();
        if (delta > 0) moving.Reverse();   // the row nearest the way it moves goes first
        var steps = new List<(string Name, int Index)>();
        foreach (var name in moving)
        {
            var from = list.FindIndex(n => Same(n, name));
            var to = from + delta;
            if (to < 0 || to >= list.Count || set.Contains(list[to])) continue;
            list.RemoveAt(from);
            list.Insert(to, name);
            steps.Add((name, to));
        }
        return steps;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
