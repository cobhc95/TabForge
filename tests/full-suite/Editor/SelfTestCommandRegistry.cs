using System.IO;
using System.Linq;
using TabForge.Services;
using TabForge.Views.EffectEditors;

namespace TabForge;

/// <summary>
/// The plain-command table (MainWindow.Commands.cs) and the order RunHotkey asks its routers in. Source-level: no window is opened.
/// Owns: registry ids are unique and routed only by the table; routers and the switch run before the table.
/// Does not own: that every catalogued id has a handler (TestEveryHotkeyIdHasHandler).
/// </summary>
public static partial class SelfTest
{
    private static void TestCommandRegistryRouting()
    {
        // The table itself: a duplicate id throws, an unknown id declines, a known id runs once.
        var table = new CommandRegistry();
        var runs = 0;
        table.Add("A.One", () => runs++);
        Check("a command registry runs a registered id once", table.TryRun("A.One") && runs == 1);
        Check("a command registry declines an unknown id", !table.TryRun("A.Two") && runs == 1);
        var duplicateThrows = false;
        try { table.Add("A.One", () => { }); } catch (InvalidOperationException) { duplicateThrows = true; }
        Check("a command registry rejects an id registered twice", duplicateThrows);

        var src = RouterSourceFolder();
        if (src is null) { Skip("command registry routing", "no source checkout found"); return; }
        var commandsText = File.ReadAllText(Path.Combine(src, "MainWindow.Commands.cs"));
        var settingsText = File.ReadAllText(Path.Combine(src, "MainWindow.Settings.cs"));
        var registered = CommandTableLine.Matches(commandsText).Select(m => m.Groups[1].Value).ToList();
        var duplicates = registered.GroupBy(i => i).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Check($"the command table registers {registered.Count} ids, none twice", registered.Count > 50 && duplicates.Count == 0, string.Join(", ", duplicates));

        // Every registered id is catalogued, and only the table runs it (not a router, the switch or the clip path).
        var catalog = HotkeyCatalog.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var otherRoutes = new HashSet<string>(StringComparer.Ordinal);
        RouterSourceScan.AddRouteIds(RouterSourceScan.ScoreCommandFiles(src).Append(Path.Combine(src, "MainWindow.Settings.cs")), otherRoutes);
        var uncatalogued = registered.Where(i => !catalog.Contains(i)).ToList();
        Check("every command table id is a catalogued command", uncatalogued.Count == 0, string.Join(", ", uncatalogued));
        var shadowed = registered.Where(i => otherRoutes.Contains(i) || EffectEditorFlow.KindOf(i) is not null || HotkeyCatalog.IsClipAction(i)
            || HotkeyCatalog.IsRangeAction(i) || i.StartsWith("Tool.", StringComparison.Ordinal)).ToList();
        Check("a command table id is not also handled by a router, the switch or the clip path (so each id has one route)", shadowed.Count == 0, string.Join(", ", shadowed));

        // Precedence: routers, then the switch, then the table, so an id in both a router and the table would run the router.
        var body = settingsText[settingsText.IndexOf("private bool RunHotkey(string id)", StringComparison.Ordinal)..];
        var order = new[] { "TryRunNoteCommand(id)", "RunPaneHotkey(id)", "RunEffectEditorHotkey(id)", "switch (id)", "Commands.TryRun(id)" }.Select(t => body.IndexOf(t, StringComparison.Ordinal)).ToList();
        Check("RunHotkey asks the routers, then the switch, then the command table", order.All(i => i >= 0) && order.SequenceEqual(order.OrderBy(i => i)), string.Join(",", order));
    }
}
