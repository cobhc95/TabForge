using System.IO;
using System.Windows.Controls;
using TabForge.Audio;
using TabForge.Services;
using TabForge.Views;

namespace TabForge;

// Owns: the checks on the main-menu table (MainWindow.Menus.cs): every row id is a catalogued command, ids and handlers are not repeated,
//   and MainWindow.xaml keeps no plain command item (those belong in the table). Also the helper that runs a check on a real MainWindow.
// Does not own: the menu tree itself (TestMainMenuTreeGolden).
// Tests: TestMainMenuTable.
public static partial class SelfTest
{
    /// <summary>Builds the real main window off-screen, runs <paramref name="check"/> on it and closes it.</summary>
    private static void WithMainWindow(Action<MainWindow> check)
    {
        using var alive = KeepAlive();
        var window = new MainWindow(AudioEngineClient.Instance, new Shell.AppOptions());
        try { ShowTestWindow(window); check(window); }
        finally { foreach (var s in window.OpenDocuments.ToList()) s.MarkClean(); window.Close(); }
    }

    private static void TestMainMenuTable()
    {
        WithMainWindow(window =>
        {
            // Building the menus must not build the command table: it is built at the first hotkey or menu use.
            var commandTable = typeof(MainWindow).GetField("_commandTable", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            Check("menu table: building the window does not build the command table", commandTable.GetValue(window) is null);
            var rows = window.MainMenuGroups().SelectMany(g => g.Rows).Where(r => !r.IsSeparator).ToList();
            Check("menu table: has rows", rows.Count > 100, $"{rows.Count} rows");
            var unknown = rows.Where(r => r.Id is not null && HotkeyCatalog.ById(r.Id) is null).Select(r => r.Id).ToList();
            Check("menu table: every row id exists in the command catalogue", unknown.Count == 0, string.Join(", ", unknown));
            var dupIds = rows.Where(r => r.Id is not null).GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            Check("menu table: no command id appears twice", dupIds.Count == 0, string.Join(", ", dupIds));
            Check("menu table: every row has a handler", rows.All(r => r.Click is not null));
            var built = new List<string?>();
            void Walk(ItemsControl parent) { foreach (var m in parent.Items.OfType<MenuItem>()) { built.Add(MenuHotkey.GetId(m)); Walk(m); } }
            Walk(window.MainMenu);
            Check("menu table: every row id is on a live menu item", rows.Where(r => r.Id is not null).All(r => built.Contains(r.Id)));
        });

        var xamlPath = FindMainWindowXaml();
        if (xamlPath is null) { Skip("menu table: skeleton audit", "MainWindow.xaml not next to the build"); return; }
        System.Xml.Linq.XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        System.Xml.Linq.XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var menu = System.Xml.Linq.XDocument.Load(xamlPath).Descendants(wpf + "Menu").First(m => (string?)m.Attribute(x + "Name") == "MainMenu");
        var plain = menu.Descendants(wpf + "MenuItem")
            .Where(m => m.Attribute("Click") is not null && m.Attribute(x + "Name") is null && m.Attribute("IsCheckable") is null)
            .Select(m => (string?)m.Attribute("Header")).ToList();
        Check("menu table: MainWindow.xaml has no plain command item (add one MenuRow in MainWindow.Menus.cs instead)", plain.Count == 0, string.Join(", ", plain));
    }
}
