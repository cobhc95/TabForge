using System.Windows;
using System.Windows.Controls;
using TabForge.Models;
using TabForge.Services;
using TabForge.Services.Features;
using TabForge.Views;
using TabForge.Views.Fx;

namespace TabForge;

// Owns: the FX chain window's commands: each has its catalogue row with the key the window had, the FX menu and the container menu show
//     the live binding (no literal gesture text), a rebind changes the key route and the menu text, and the FX scope may share a key with a main-window command.
// Does not own: what each command does (TestFxChainWindow, TestFxChainContainerRows), the conflict dialog (TestHotkeyPage).
// Tests: TestFxChainCommandScope.
public static partial class SelfTest
{
    private static void TestFxChainCommandScope()
    {
        var w = SmNewWindow();
        var previous = TooltipShortcuts.Hotkeys;
        Window? fx = null;
        try
        {
            var doc = SmOpenSong(w, SmDemoSong());
            var track = doc.Project.Tracks[0];
            w.MixerHost.OpenFxChain(track);
            fx = w.MixerHost.Windows.FxWindowOf(track) ?? throw new InvalidOperationException("the FX window did not open");
            SmSettle();

            // The catalogue: every FX command is in the FX scope, with the key the window had (Ctrl+S, Ctrl+O, Ctrl+G, Ctrl+Shift+G, F2) and none elsewhere.
            var defaults = new Dictionary<string, string>
            {
                [FxChainIds.Save] = "Ctrl+S", [FxChainIds.Load] = "Ctrl+O", [FxChainIds.GroupSelected] = "Ctrl+G",
                [FxChainIds.Dissolve] = "Ctrl+Shift+G", [FxChainIds.Rename] = "F2",
                [FxChainIds.AddPlugin] = "", [FxChainIds.RemovePlugin] = "", [FxChainIds.AddToEnd] = "", [FxChainIds.Clear] = "",
                [FxChainIds.SaveForHosts] = "", [FxChainIds.NewContainer] = "", [FxChainIds.DeleteContainer] = "",
            };
            var wrongDefaults = defaults.Where(p => HotkeyCatalog.ById(p.Key) is not { } a || a.DefaultGesture != p.Value || !HotkeyCatalog.IsFxChainAction(p.Key)).Select(p => p.Key).ToList();
            Check($"each of the {defaults.Count} FX chain commands is catalogued in the FX scope with its default key", wrongDefaults.Count == 0, string.Join(", ", wrongDefaults));

            // The menus: every item of the FX menu and the container menu that runs a command shows that command's live key; no other item shows one.
            var menu = ((DockPanel)fx.Content).Children.OfType<Menu>().First();
            var settings = new HotkeySettings();
            TooltipShortcuts.SetHotkeys(settings);
            var items = new List<MenuItem>();
            void Collect(ItemsControl parent) { foreach (var item in parent.Items.OfType<MenuItem>()) { items.Add(item); Collect(item); } }
            Collect(menu);
            var containers = LtField<FxChainContainers>(fx, "_containers")!;
            items.AddRange(containers.MenuItems());
            FxChainKeys.Apply(menu);
            var literal = items.Where(i => MenuHotkey.GetId(i) is { Length: > 0 } id
                ? i.InputGestureText != HotkeyCatalog.DisplayAll(settings, id)
                : !string.IsNullOrEmpty(i.InputGestureText)).Select(i => i.Header?.ToString() ?? "").ToList();
            Check($"the FX and container menu items show the live key of their command ({items.Count} items, none with a literal key)", literal.Count == 0, string.Join(", ", literal));
            var save = MenuHotkey.Find(menu.Items, FxChainIds.Save);
            Check("Save FX chain shows Ctrl+S from the bindings", save?.InputGestureText == "Ctrl+S", save?.InputGestureText);

            // A rebind (Save FX chain to Ctrl+Shift+S) changes the key route and the menu text; the old key is free in the FX scope.
            settings.Set(FxChainIds.Save, 1, "Ctrl+Shift+S");
            TooltipShortcuts.SetHotkeys(settings);
            FxChainKeys.Apply(menu);
            Check("a rebind moves the FX key route: Ctrl+Shift+S runs Save, Ctrl+S no longer does",
                FxChainKeys.IdFor(settings, "Ctrl+Shift+S") == FxChainIds.Save && FxChainKeys.IdFor(settings, "Ctrl+S") is null);
            Check("a rebind changes the menu text to the new key", MenuHotkey.Find(menu.Items, FxChainIds.Save)?.InputGestureText == "Ctrl+Shift+S",
                MenuHotkey.Find(menu.Items, FxChainIds.Save)?.InputGestureText);

            // The same key in two scopes: Ctrl+S is File.Save in the main window and FxChain.Save in this window, and the rule accepts it.
            var mainMap = HotkeyCatalog.BuildMap(new HotkeySettings());
            var fxMap = HotkeyCatalog.BuildMap(new HotkeySettings(), fxChainContext: true);
            Check("Ctrl+S is a main-window command and an FX chain command at once, and no FX command leaks into the main window",
                mainMap.TryGetValue("Ctrl+S", out var mainId) && mainId == "File.Save" && fxMap.TryGetValue("Ctrl+S", out var fxId) && fxId == FxChainIds.Save
                && !mainMap.Values.Any(HotkeyCatalog.IsFxChainAction));
            Check("the conflict rule accepts the same key in two window scopes (different contexts)",
                !HotkeyCatalog.SameContext(FxChainIds.Save, "File.Save") && !HotkeyCatalog.SameContext(FxChainIds.GroupSelected, "Bar.GoTo"));

            // Text boxes: Save and Load keep working inside a text box, the container keys do not (as before).
            Check("text boxes keep Save and Load, and not the container keys", HotkeyCatalog.AllowsInTextBox(FxChainIds.Save) && HotkeyCatalog.AllowsInTextBox(FxChainIds.Load)
                && !HotkeyCatalog.AllowsInTextBox(FxChainIds.GroupSelected) && !HotkeyCatalog.AllowsInTextBox(FxChainIds.Rename));
        }
        finally
        {
            TooltipShortcuts.SetHotkeys(previous);
            if (fx is not null) fx.Close();
            SmCloseWindow(w);
        }
    }
}
