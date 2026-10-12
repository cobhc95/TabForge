namespace TabForge.Views.IconRow;

// Owns: the order of the top bar's icon row as a list of ids (button ids and separators): the default sequence, restoring a saved one, moving an icon, and which separators show.
// Does not own: the buttons, the mouse drag and the stored setting (IconRowDragController.cs, MainWindow.xaml, AppearanceSettings.IconRowOrder).
// Tests: TestIconRowOrder.
internal static class IconRowOrder
{
    /// <summary>The id every separator carries; separators are items of the same sequence as the icons.</summary>
    public const string Separator = "|";

    /// <summary>The default sequence, grouped by kind: side panel | instrument views | song views | project settings.</summary>
    public static readonly string[] Default = { "side", Separator, "fretboard", "keyboard", Separator, "band", "compact", Separator, "project" };

    /// <summary>
    /// The sequence to show for a saved one: unknown and repeated ids are dropped, icons the saved list lacks are appended in
    /// default order, and the separator count is that of <paramref name="defaults"/>. Nothing saved gives the defaults.
    /// </summary>
    public static List<string> Apply(IEnumerable<string>? saved, IReadOnlyList<string> defaults)
    {
        var icons = defaults.Where(d => d != Separator).ToList();
        var separators = defaults.Count - icons.Count;
        var result = new List<string>();
        foreach (var id in saved ?? Enumerable.Empty<string>())
            if (id == Separator ? result.Count(r => r == Separator) < separators : icons.Contains(id) && !result.Contains(id)) result.Add(id);
        if (result.All(r => r == Separator)) return defaults.ToList();
        result.AddRange(icons.Where(i => !result.Contains(i)));
        while (result.Count < defaults.Count) result.Add(Separator);
        return result;
    }

    /// <summary>The sequence with the icon at <paramref name="from"/> inserted before the item now at <paramref name="to"/> (the count = the end). Separators do not move.</summary>
    public static List<string> Move(IReadOnlyList<string> order, int from, int to)
    {
        var result = order.ToList();
        if (from < 0 || from >= result.Count || result[from] == Separator) return result;
        result.RemoveAt(from);
        result.Insert(Math.Clamp(to > from ? to - 1 : to, 0, result.Count), order[from]);
        return result;
    }

    /// <summary>
    /// Per item: true for a separator that shows. A separator shows only between two shown icons, and only the first of a run,
    /// so the row has no leading, trailing or doubled line. <paramref name="iconShown"/> tells whether the icon at an index takes space.
    /// </summary>
    public static bool[] SeparatorShown(IReadOnlyList<string> order, Func<int, bool>? iconShown = null)
    {
        var result = new bool[order.Count];
        bool Icon(int i) => order[i] != Separator && (iconShown?.Invoke(i) ?? true);
        var iconBefore = false;
        for (var i = 0; i < order.Count; i++)
            if (order[i] != Separator) iconBefore |= Icon(i);
            else if (iconBefore && Enumerable.Range(i + 1, order.Count - i - 1).Any(Icon)) { result[i] = true; iconBefore = false; }
        return result;
    }
}
