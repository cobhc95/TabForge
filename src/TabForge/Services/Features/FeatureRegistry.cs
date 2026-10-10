namespace TabForge.Services.Features;

// Owns: the list of feature modules and the merge of their rows into the central tables at the point each table is assembled.
// Does not own: the tables themselves or what a module contributes.
// Tests: TestFeatureModuleContributions.
public static class FeatureRegistry
{
    /// <summary>The modules, in the order their rows are merged. A new feature adds one line here.</summary>
    public static readonly IReadOnlyList<IFeatureModule> Modules = new IFeatureModule[]
    {
        new Band.BandFeatureModule(),
        new Video.VideoFeatureModule(),
        new Export.ExportFeatureModule(),
        new KeyboardMode.KeyboardModeFeatureModule(),
        new KeyboardMode.KeyboardModePopoutFeatureModule(),
    };

    /// <summary>Inserts every module hotkey after its anchor row.</summary>
    public static void AddHotkeys(List<HotkeyAction> all)
    {
        foreach (var module in Modules)
            foreach (var row in module.Hotkeys)
            {
                var at = all.FindIndex(a => a.Id == row.AfterId);
                if (at < 0) throw new InvalidOperationException($"Feature '{module.Name}': hotkey anchor '{row.AfterId}' not found.");
                all.Insert(at + 1, row.Action);
            }
    }

    /// <summary>The settings rows of every module.</summary>
    public static IEnumerable<SettingDescriptor> SettingRows(AppSettings settings) => Modules.SelectMany(m => m.SettingRows(settings));

    /// <summary>The layout table with every module group inserted after its anchor group.</summary>
    public static (string Page, string Group, string Keys)[] MergeLayout((string Page, string Group, string Keys)[] layout)
    {
        var list = layout.ToList();
        foreach (var module in Modules)
            foreach (var g in module.Layout)
            {
                var at = list.FindLastIndex(e => e.Page == g.Page && e.Group == g.AfterGroup);
                if (at < 0) throw new InvalidOperationException($"Feature '{module.Name}': layout anchor '{g.AfterGroup}' not found.");
                list.Insert(at + 1, (g.Page, g.Group, g.Keys));
            }
        return list.ToArray();
    }

    /// <summary>Registers every module command.</summary>
    public static void AddCommands(CommandRegistry table, IFeatureHost host)
    {
        foreach (var module in Modules)
            foreach (var c in module.Commands(host)) table.Add(c.Id, c.Run);
    }

    /// <summary>Runs every module's settings bounds.</summary>
    public static void Normalize(AppSettings settings)
    {
        foreach (var module in Modules) module.Normalize(settings);
    }

    /// <summary>All module menu rows.</summary>
    public static IEnumerable<FeatureMenuRow> MenuRows => Modules.SelectMany(m => m.MenuRows);
}
