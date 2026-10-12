using TabForge.Views.IconRow;

namespace TabForge;

/// <summary>The top bar's icon row: its order rules (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    /// <summary>The default groups, moving an icon across a separator and to both ends, separators never dangling or doubled, and restoring a saved order.</summary>
    private static void TestIconRowOrder()
    {
        const string S = IconRowOrder.Separator;
        var def = IconRowOrder.Default;
        static string Show(IReadOnlyList<string> order)
        {
            var shown = IconRowOrder.SeparatorShown(order);
            return string.Join(" ", order.Where((id, i) => id != IconRowOrder.Separator || shown[i]));
        }
        bool Tidy(IReadOnlyList<string> order)
        {
            var shown = Show(order).Split(' ');
            return shown[0] != S && shown[^1] != S && !shown.Zip(shown.Skip(1)).Any(p => p.First == S && p.Second == S)
                && order.Count == def.Length && order.Where(id => id != S).OrderBy(id => id).SequenceEqual(def.Where(id => id != S).OrderBy(id => id));
        }

        Check("icon row: the default is side | fretboard keyboard | band compact | project", Show(def) == "side | fretboard keyboard | band compact | project", Show(def));
        Check("icon row: nothing saved, and an empty list, give the default", IconRowOrder.Apply(null, def).SequenceEqual(def) && IconRowOrder.Apply(new List<string>(), def).SequenceEqual(def));

        var across = IconRowOrder.Move(def, Array.IndexOf(def, "band"), 1);   // just before the first separator
        Check("icon row: an icon dropped before a separator lands on that side of it", Show(across) == "side band | fretboard keyboard | compact | project", Show(across));
        var after = IconRowOrder.Move(def, 0, 5);   // just after the second separator
        Check("icon row: an icon dropped after a separator lands on that side, and the leading separator hides", Show(after) == "fretboard keyboard | side band compact | project", Show(after));
        var first = IconRowOrder.Move(def, def.Length - 1, 0);
        var last = IconRowOrder.Move(def, 0, def.Length);
        Check("icon row: an icon moves to the start", Show(first) == "project side | fretboard keyboard | band compact", Show(first));
        Check("icon row: an icon moves to the end", Show(last) == "fretboard keyboard | band compact | project side", Show(last));
        Check("icon row: a separator does not move", IconRowOrder.Move(def, 1, 0).SequenceEqual(def));

        var untidy = 0;
        for (var from = 0; from < def.Length; from++)
            for (var to = 0; to <= def.Length; to++)
            {
                var once = IconRowOrder.Move(def, from, to);
                for (var to2 = 0; to2 <= def.Length; to2++)
                    if (!Tidy(once) || !Tidy(IconRowOrder.Move(once, 0, to2)) || !Tidy(IconRowOrder.Move(once, def.Length - 1, to2))) untidy++;
            }
        Check("icon row: no move leaves a leading, trailing or doubled separator, or loses an item", untidy == 0, $"{untidy}");
        var hidden = IconRowOrder.SeparatorShown(def, i => def[i] != "side");
        Check("icon row: a separator beside a hidden icon's empty group hides", !hidden[1] && hidden[4] && hidden[7]);

        var saved = IconRowOrder.Apply(new[] { "project", S, "ghost", "band", "band", S, S, S, "side" }, def);
        Check("icon row: an unknown or repeated id is dropped and missing icons are appended in default order",
            saved.SequenceEqual(new[] { "project", S, "band", S, S, "side", "fretboard", "keyboard", "compact" }) && Tidy(saved), string.Join(" ", saved));
        Check("icon row: a saved order comes back as saved", IconRowOrder.Apply(across, def).SequenceEqual(across));
        Check("icon row: reset gives the default", IconRowOrder.Apply(null, def).SequenceEqual(IconRowOrder.Default) && new TabForge.Services.AppearanceSettings().IconRowOrder.Count == 0);
    }
}
