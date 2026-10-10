using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using TabForge.Audio;
using TabForge.Services;
using TabForge.Views;
using TabForge.Views.Band;

namespace TabForge;

// Owns: the canonical text snapshot of the main menu tree (built from the real MainWindow) and of the code-built menus (track row, bar,
//   selection, section, clip, fretboard, score, band), compared with tests/full-suite/Menus/*.golden.txt.
// Does not own: the menus themselves (MainWindow.xaml, TrackRowMenus, TimelineContextMenus, ContextMenuSpecs).
// Tests: TestMainMenuTreeGolden. Regenerate with TABFORGE_RECORD_MENU_GOLDEN=1 (docs/RECIPES.md).
public static partial class SelfTest
{
    private const string MenuGoldenMain = "main-menu-tree.golden.txt", MenuGoldenCode = "code-menus.golden.txt";

    private static void TestMainMenuTreeGolden()
    {
        using var alive = KeepAlive();
        var root = FindRepositoryRoot();
        var dir = root is null ? null : Path.Combine(root, "tests", "full-suite", "Menus");
        if (dir is null || !Directory.Exists(dir)) { Skip("menu golden: golden files","tests/full-suite/Menus not next to the build"); return; }

        var window = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
        string main;
        try
        {
            ShowTestWindow(window);
            main = DumpMainMenu(window.MainMenu);
        }
        finally { foreach (var s in window.OpenDocuments.ToList()) s.MarkClean(); window.Close(); }
        CompareMenuGolden(dir, MenuGoldenMain, main);
        CompareMenuGolden(dir, MenuGoldenCode, DumpCodeMenus());
    }

    private static void CompareMenuGolden(string dir, string file, string actual)
    {
        var path = Path.Combine(dir, file);
        if (Environment.GetEnvironmentVariable("TABFORGE_RECORD_MENU_GOLDEN") == "1")
        {
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            Check($"menu golden: {file} recorded ({actual.Split('\n').Length} lines)", true);
            return;
        }
        var expected = File.Exists(path) ? File.ReadAllText(path).Replace("\r\n", "\n") : "";
        var a = actual.Split('\n'); var e = expected.Split('\n');
        var at = Enumerable.Range(0, Math.Min(a.Length, e.Length)).FirstOrDefault(i => a[i] != e[i], -1);
        if (at < 0 && a.Length != e.Length) at = Math.Min(a.Length, e.Length);
        Check($"menu golden: {file} is unchanged", at < 0,
            $"first difference at line {at + 1}: expected \"{(at >= 0 && at < e.Length ? e[at] : "<end>")}\", got \"{(at >= 0 && at < a.Length ? a[at] : "<end>")}\"; set TABFORGE_RECORD_MENU_GOLDEN=1 to re-record");
    }

    /// <summary>One line per item, two spaces of indent per nesting level. Checked / enabled state is not recorded (it follows settings and the document).</summary>
    private static string DumpMainMenu(Menu menu)
    {
        var sb = new StringBuilder();
        int items = 0, separators = 0;
        void Walk(ItemsControl parent, int depth)
        {
            foreach (var child in parent.Items)
            {
                var pad = new string(' ', depth * 2);
                if (child is Separator) { sb.Append(pad).Append("---\n"); separators++; continue; }
                if (child is not MenuItem m) { sb.Append(pad).Append('<').Append(child?.GetType().Name ?? "null").Append(">\n"); continue; }
                items++;
                var enabledBinding = BindingOperations.GetBindingBase(m, UIElement.IsEnabledProperty) is Binding b ? "binding:" + b.Path?.Path : BindingOperations.IsDataBound(m, UIElement.IsEnabledProperty) ? "binding" : "";
                sb.Append(pad).Append(m.Header is string h ? h : m.Header?.GetType().Name ?? "")
                  .Append(" | name=").Append(m.Name)
                  .Append(" | id=").Append(MenuHotkey.GetId(m))
                  .Append(" | tag=").Append(m.Tag)
                  .Append(" | gesture=").Append(m.InputGestureText)
                  .Append(" | checkable=").Append(m.IsCheckable ? "yes" : "no")
                  .Append(" | icon=").Append(m.Icon is null ? "no" : "yes")
                  .Append(" | enabled=").Append(enabledBinding)
                  .Append('\n');
                Walk(m, depth + 1);
            }
        }
        Walk(menu, 0);
        return $"# main menu: {items} items, {separators} separators; regenerate with TABFORGE_RECORD_MENU_GOLDEN=1\n" + sb;
    }

    private static string DumpCodeMenus()
    {
        var sb = new StringBuilder("# code-built menus (MenuSpec data), several states each; regenerate with TABFORGE_RECORD_MENU_GOLDEN=1\n");
        var keys = new HotkeySettings();
        string Key(string id) => HotkeyCatalog.DisplayAll(keys, id);
        void Dump(string title, List<MenuSpec> specs)
        {
            sb.Append("== ").Append(title).Append('\n');
            void Walk(List<MenuSpec> list, int depth)
            {
                foreach (var s in list)
                {
                    var pad = new string(' ', depth * 2);
                    if (s.IsSeparator) { sb.Append(pad).Append("---\n"); continue; }
                    sb.Append(pad).Append(s.Header).Append(" | key=").Append(s.Shortcut).Append(" | id=").Append(s.Id).Append(" | arg=").Append(s.Arg)
                      .Append(" | cmd=").Append(s.Command).Append(" | enabled=").Append(s.Enabled).Append(" | checkable=").Append(s.Checkable)
                      .Append(" | radio=").Append(s.Radio).Append(" | label=").Append(s.IsLabel).Append(" | setting=").Append(s.SettingKey ?? s.NoSetting)
                      .Append(" | tip=").Append(s.ToolTip).Append('\n');
                    if (s.Children is not null) Walk(s.Children, depth + 1);
                }
            }
            Walk(specs, 0);
        }
        Dump("track row, instrument", TrackRowMenus.Build(new TrackRowMenuState(false, true, true, "#F61A16"), Key));
        Dump("track row, audio", TrackRowMenus.Build(new TrackRowMenuState(true, false, true, "#7CC4F2"), Key));
        Dump("track row, last track", TrackRowMenus.Build(new TrackRowMenuState(false, false, false, ""), Key));
        Dump("bar, full", TimelineMenus.Bar(new BarMenuState(true, true, 3, true, true, true, false, true, true, true), Key));
        Dump("bar, none", TimelineMenus.Bar(new BarMenuState(false, false, 1, false, false, false, false, false, false), Key));
        Dump("selection, active", TimelineMenus.Selection(new SelectionMenuState("2 bars", true, true, true, true, 2), Key));
        Dump("selection, plain", TimelineMenus.Selection(new SelectionMenuState("1 bar", false, false, false, false), Key));
        Dump("section, at bar", TimelineMenus.Section(new SectionMenuState(3, true, true, true), Key));
        Dump("section, inside", TimelineMenus.Section(new SectionMenuState(null, false, false, false), Key));
        Dump("clip, audio", TimelineMenus.Clip(new ClipMenuState(true, false, true, false), Key));
        Dump("clip, midi muted", TimelineMenus.Clip(new ClipMenuState(true, true, false, true), Key));
        Dump("clip, none", TimelineMenus.Clip(new ClipMenuState(false, false, true, false), Key));
        var roots = new[] { "C", "D", "E" }; var scales = new[] { "Major", "Minor" };
        Dump("fretboard", InstrumentMenus.Build(new InstrumentMenuState(false, false, "Gtr", "Standard", new[] { "Standard", "Notes" }, roots, scales, "Major", true, true, false, false), Key));
        Dump("keyboard", InstrumentMenus.Build(new InstrumentMenuState(true, false, "Keys", "Keys", new[] { "Keys" }, roots, scales, null, false, false, true, true, true), Key));
        Dump("drums", InstrumentMenus.Build(new InstrumentMenuState(false, true, "Drums", "Pads", new[] { "Pads" }, roots, scales, null, false, false, false, false), Key));
        Dump("score, over beat", ScoreMenus.Empty(new ScoreEmptyState(true, true, true, true, false), Key));
        Dump("score, empty area", ScoreMenus.Empty(new ScoreEmptyState(false, false, false, false, true), Key));
        Dump("band", BandMenus.Build(new BandMenuState(BandChoices.Contents[0], BandChoices.Sizes[0], BandLayoutState.MinRowsPerScreen, true), Key));
        Dump("band, per row", BandMenus.Build(new BandMenuState(BandChoices.Contents[^1], BandChoices.Sizes[^1], BandLayoutState.MaxRowsPerScreen, false, true, BandChoices.PlayheadLines[^1], BandChoices.Layouts[^1]), Key));
        return sb.ToString();
    }
}
