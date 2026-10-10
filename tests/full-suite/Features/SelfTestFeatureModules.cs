using TabForge.Services;
using TabForge.Services.Features;

namespace TabForge;

// Owns: the check that every feature-module contribution has a unique id and lands in exactly one central table.
// Does not own: the tables' own tests (TestPreferencesCatalog, TestMainMenuTable, TestEveryHotkeyIdHasHandler).
// Tests: TestFeatureModuleContributions.
public static partial class SelfTest
{
    private sealed class StubFeatureHost : IFeatureHost, TabForge.Services.Video.IVideoCommandHost, TabForge.Services.Band.IBandCommandHost, TabForge.Services.Export.IExportCommandHost, TabForge.KeyboardMode.IKeyboardModeCommandHost
    {
        public void ToggleKeyboardMode() { }
        public TabForge.KeyboardMode.IKeyboardModeWaitCommands WaitCommands => null!;
        public void ExportVideo() { }
        public void ToggleVideoRecording() { }
        public void RunBandCommand(string id) { }
        public void SaveAsTemplate() { }
        public void ExportPdf() { }
        public void ExportMusicXml() { }
        public void ExportMidi() { }
        public void ExportAscii() { }
        public void ExportGuitarPro() { }
    }

    private static void TestFeatureModuleContributions()
    {
        var modules = FeatureRegistry.Modules;
        Check("feature modules: names are unique", modules.Select(m => m.Name).Distinct().Count() == modules.Count);

        var hotkeyIds = modules.SelectMany(m => m.Hotkeys).Select(h => h.Action.Id).ToList();
        Check("feature modules: hotkey ids are unique", hotkeyIds.Distinct().Count() == hotkeyIds.Count);
        Check("feature modules: each hotkey is in the catalogue exactly once",
            hotkeyIds.All(id => HotkeyCatalog.All.Count(a => a.Id == id) == 1));

        var table = new CommandRegistry();
        FeatureRegistry.AddCommands(table, new StubFeatureHost());   // throws on a repeated id
        Check("feature modules: every command id is a catalogued hotkey", table.Ids.All(id => HotkeyCatalog.ById(id) is not null), string.Join(",", table.Ids));
        var menuIds = FeatureRegistry.MenuRows.Select(r => r.Id).ToList();
        Check("feature modules: menu ids are unique and have a command", menuIds.Distinct().Count() == menuIds.Count && menuIds.All(id => table.Ids.Contains(id)));

        var exportModule = modules.Single(m => m.Name == "Export");
        var exportCommands = exportModule.Commands(new StubFeatureHost()).Select(c => c.Id).ToList();
        Check("export module: a command for every hotkey and menu row it adds",
            exportModule.Hotkeys.All(h => exportCommands.Contains(h.Action.Id)) && exportModule.MenuRows.All(r => exportCommands.Contains(r.Id)),
            string.Join(",", exportCommands));

        var keys = SettingsCatalog.Build(new AppSettings()).Select(d => d.Key).ToList();
        var moduleKeys = modules.SelectMany(m => m.SettingRows(new AppSettings())).Select(d => d.Key).ToList();
        Check("feature modules: setting keys are unique and in the catalogue once",
            moduleKeys.Distinct().Count() == moduleKeys.Count && moduleKeys.All(k => keys.Count(x => x == k) == 1));
        Check("feature modules: each setting row has a place in the layout", moduleKeys.All(k => SettingsCatalog.PlaceOf(k) is not null));
        Check("feature modules: the experimental video encoder rows are in the catalogue", new[] { "video.encoder", "video.fastexport", "video.lowlatency" }.All(k => moduleKeys.Contains(k)));

        var hotkeyOrder = HotkeyCatalog.All.Select(a => a.Id).ToList();
        var bandRows = new[] { "Band.ToggleTrackRow", "Band.RowsMore", "Band.RowsFewer", "Band.CycleLaneContent", "Band.CycleLaneLayout", "Band.CycleInstrumentSize", "Band.ToggleSmoothFollow", "Band.ResetRowHeights" };
        var anchor = hotkeyOrder.IndexOf("View.BandView");
        Check("feature modules: the Band hotkey rows follow View.BandView in order", anchor >= 0 && bandRows.Select((id, i) => hotkeyOrder.IndexOf(id) == anchor + 1 + i).All(ok => ok));
        Check("feature modules: the Band commands are registered", bandRows.All(id => table.Ids.Contains(id)));
    }
}
