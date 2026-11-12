using System;
using System.Linq;
using System.Reflection;
using TabForge.Avalonia.Features.LeftStrip;
using TabForge.Menus;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge;

// Owns: the check that the command ids a menu row, a left-strip button or a CommandIds constant names are ids the catalogue knows (a mistyped id compiles and then does nothing).
// Does not own: the handlers behind the ids (MainWindowMenuTests, HotkeySweepTests), the key bindings or the ids themselves (CommandIds, HotkeyCatalog, MainMenuTable).
// Tests: TestCommandIdsAreInTheCatalogue.
public static partial class SelfTest
{
    /// <summary>
    /// The catalogue is the hotkey catalogue (every bindable command) plus the ids the main-menu table itself lists. Every <see cref="CommandIds"/> constant is unique and
    /// in it, and so is every id of the left strip's items and of the main-menu table that the hotkey catalogue is meant to hold.
    /// </summary>
    private static void TestCommandIdsAreInTheCatalogue()
    {
        var hotkeys = HotkeyCatalog.All.Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        var menu = MainMenuTable.CommandRows(MainMenuTable.Groups).Select(r => r.CommandId!).ToHashSet(StringComparer.Ordinal);
        var strip = LeftStripItems.All.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);

        var constants = typeof(CommandIds).GetNestedTypes(BindingFlags.Public)
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.IsLiteral && f.FieldType == typeof(string))
                .Select(f => (Name: $"{t.Name}.{f.Name}", Value: (string)f.GetRawConstantValue()!)))
            .ToList();
        Check("CommandIds: there are constants", constants.Count > 50, $"found {constants.Count}");
        Check("CommandIds: each constant is its area and name", constants.All(c => c.Name == c.Value), string.Join(", ", constants.Where(c => c.Name != c.Value).Select(c => $"{c.Name}={c.Value}")));
        Check("CommandIds: no value appears twice", constants.Select(c => c.Value).Distinct().Count() == constants.Count);
        var unknown = constants.Where(c => !hotkeys.Contains(c.Value) && !menu.Contains(c.Value) && !strip.Contains(c.Value)).Select(c => c.Name).ToList();
        Check("CommandIds: every constant is a hotkey, menu-table or strip id", unknown.Count == 0, string.Join(", ", unknown));

        var menuMissing = menu.Where(id => !hotkeys.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Check("main menu: every row's command id is in the hotkey catalogue", menuMissing.Count == 0, string.Join(", ", menuMissing));
        var stripMissing = strip.Where(id => !hotkeys.Contains(id) && !menu.Contains(id)).OrderBy(id => id, StringComparer.Ordinal).ToList();
        Check("left strip: every item's command id is a hotkey or menu id", stripMissing.Count == 0, string.Join(", ", stripMissing));
    }
}
