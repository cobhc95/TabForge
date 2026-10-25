using System.Collections.Generic;
using TabForge.Services;

namespace TabForge.Docking;

// Owns: saving the current dock as a named layout, deleting one saved layout, and removing the built-in layouts' saved copies.
//   These are the rules the View > Layouts rows apply to the settings list; the caller passes the list and saves the settings.
// Does not own: the menus or prompts (the toolkit windows), the layout names and the built-in set (DockLayoutCatalog), the settings file.
// Tests: no dedicated test class.
internal static class SavedLayoutRules
{
    /// <summary>Saves <paramref name="state"/> under the typed name, replacing a saved layout of that name. Returns the saved entry, or null
    /// when the name is empty or the list is full and the name is new.</summary>
    public static SavedLayout? SaveCurrent(List<SavedLayout> saved, string typed, DockWorkspaceState state, bool maximised, double width, double height)
    {
        var trimmed = typed.Trim();
        if (trimmed.Length == 0) return null;
        var name = DockLayoutCatalog.SavedLayoutName(trimmed);
        var entry = DockLayoutCatalog.FindSaved(saved, name);
        if (entry is null)
        {
            if (saved.Count >= DockLayoutCatalog.MaxSavedLayouts) return null;
            entry = new SavedLayout();
            saved.Add(entry);
        }
        entry.Name = name;
        entry.State = state;
        entry.Maximised = maximised;
        entry.WindowWidth = width;
        entry.WindowHeight = height;
        return entry;
    }

    /// <summary>Removes the saved layout of that name. Returns true when the name was saved.</summary>
    public static bool Delete(List<SavedLayout> saved, string name) =>
        saved.RemoveAll(l => string.Equals(l.Name, name, System.StringComparison.OrdinalIgnoreCase)) > 0;

    /// <summary>Removes the saved copies of the built-in layouts, so each built-in is shown again as shipped.</summary>
    public static void ResetBuiltIns(List<SavedLayout> saved) => saved.RemoveAll(l => DockLayoutCatalog.IsBuiltIn(l.Name));
}
