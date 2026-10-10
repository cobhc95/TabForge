using System.IO;
using System.Reflection;
using System.Windows.Controls;
using TabForge.Docking;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>
/// The dock pane table is the one place that names a dock pane: the Panels menu, the pane registration, the built-in layouts'
/// closed lists and the settings validator all read it. The golden lines pin the ids, titles and order that saved layouts depend on.
/// </summary>
public static partial class SelfTest
{
    private const string DockMenuGolden = "tools=Tools,structure=Structure,rhythm=Rhythm,layout=Layout,sections=Sections,instrument=Fretboard,timeline=Arrangement,band=Band view,learn=Keyboard mode (experimental)";
    private const string DockRegistrationGolden =
        "instrument|Fretboard|360|-|instrument|score-editor|False;timeline|Arrangement|440|112|timeline|score-editor|False;band|Band|520|240|band|score-editor|True;" +
        "tools|Tools|210|150|tools|structure|False;structure|Structure|210|150|tools|tools|False;rhythm|Rhythm|210|140|tools|tools|False;" +
        "layout|Layout|210|140|tools|tools|False;sections|Sections|190|180|side|score-editor|False;learn|Keyboard mode (experimental)|440|200|band|score-editor|True";

    private static void TestDockPaneTable() => RunInWindowFixture((w, context) =>
    {
        w.Width = 1400; w.Height = 900;
        for (var i = 0; i < 3; i++) { w.UpdateLayout(); PumpUi(); }
        var menu = string.Join(",", w.DockPanelsMenu.Items.OfType<MenuItem>().Select(m => $"{m.Tag}={m.Header}"));
        Check("dock table: the Panels menu lists the same ids, titles and order as before", menu == DockMenuGolden, menu);

        var dock = VisualDescendants<DockWorkspace>(w).First();
        var panels = (System.Collections.IDictionary)typeof(DockWorkspace).GetField("_panels", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(dock)!;
        string Field(object r, string n) => r.GetType().GetProperty(n)!.GetValue(r)!.ToString()!;
        var reg = string.Join(";", panels.Values.Cast<object>().Select(r =>
            $"{Field(r, "Id")}|{Field(r, "Title")}|{Field(r, "MinWidth")}|{(Field(r, "Id") == "instrument" ? "-" : Field(r, "MinHeight"))}|{Field(r, "DefaultHost")}|{Field(r, "DefaultAnchor")}|{Field(r, "StartsClosed")}"));
        Check("dock table: panels are registered with the same titles, minimums, hosts and order as before", reg == DockRegistrationGolden, reg);

        var rows = DockPaneTable.Rows;
        Check("dock table: no duplicate pane ids and no duplicate registration slots", rows.Select(r => r.Id).Distinct().Count() == rows.Count && rows.Select(r => r.RegisterOrder).Distinct().Count() == rows.Count);
        Check("dock table: the table matches the registered panes in order", string.Join(";", DockPaneTable.InRegistrationOrder.Select(r =>
            $"{r.Id}|{r.PaneTitle}|{r.MinWidth}|{(r.Id == "instrument" ? "-" : r.MinHeight.ToString())}|{r.DefaultHost}|{r.DefaultAnchor}|{r.StartsClosed}")) == DockRegistrationGolden);
        Check("dock table: every pane id is a valid id in a saved layout (the settings validator reads the table)", rows.All(r => DockPaneTable.IsSavedId(r.Id)) && !DockPaneTable.IsSavedId("nonsense"));
        Check("dock table: the side panel holds the five side panes", string.Join(",", DockPaneTable.SideIds) == "tools,structure,rhythm,layout,sections");
        Check("dock table: each row's pane is registered with the dock", rows.All(r => panels.Contains(r.Id)) && panels.Count == rows.Count);

        var allIds = DockMenuGolden.Split(',').Select(p => p.Split('=')[0]).ToList();
        foreach (var name in DockLayoutController.BuiltInLayoutNames)
        {
            var state = DockLayoutController.BuiltInLayout(name);
            var present = DockWorkspace.PanelsOf(state).Concat(state.ClosedPanels).OrderBy(x => x).ToList();
            Check($"dock table: built-in layout {name} accounts for every pane exactly once", present.SequenceEqual(allIds.OrderBy(x => x)), string.Join(",", present));
        }

        var file = Path.Combine(Path.GetTempPath(), "tf-dock-table-" + Guid.NewGuid().ToString("N")[..8] + ".json");
        try
        {
            foreach (var name in DockLayoutController.BuiltInLayoutNames)
            {
                var settings = new AppSettings { Workspace = DockLayoutController.BuiltInLayout(name) };
                SettingsFileService.SaveAtomic(file, settings);
                var back = SettingsFileService.Load(file);
                Check($"dock table: layout {name} round-trips through the settings file with the same panels and closed list",
                    back.Workspace is { } ws && string.Join(",", DockWorkspace.PanelsOf(ws)) == string.Join(",", DockWorkspace.PanelsOf(settings.Workspace!))
                    && string.Join(",", ws.ClosedPanels) == string.Join(",", settings.Workspace!.ClosedPanels));
            }
        }
        finally { try { File.Delete(file); } catch (IOException) { } }
    });
}
