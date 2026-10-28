using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Plugins;
using TabForge.Presenters;

// Owns: the changes the preset manager can make (rename, duplicate, delete with an undo, move, drop on a row, import and export), each through an
//   IPresetStore and the shared Core rules (PresetManagePresenter, PresetRules, PresetBuiltInCopy).
// Does not own: the list and its selection (PresetManagerWindow), the files' rules (PresetFolderStore, DrumMapStore) or the question box
//   (PresetManagerDialogs).
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>What a change did: whether it was made, why not, the name to select afterwards, and how to take a delete back (null when it cannot be).</summary>
public sealed record PresetActionResult(bool Ok, string? Error = null, string? Select = null, Action? Undo = null);

/// <summary>The manager's changes over the injected stores, with the questions asked through the injected dialogs.</summary>
public sealed class PresetManagerActions
{
    private const string ReservedText = "A preset that comes with the app has this name. Choose another name.";
    private const string ReplaceQuestionFormat = "A preset named \"{0}\" already exists. Replace it?";
    private const string ChangeFailed = "The preset could not be changed: ";
    private const string NotImportable = "Open one plug-in or processor first: presets are imported into the place that is shown.";
    private const string NotAPresetFormat = "\"{0}\" is not a preset of this kind.";
    private const string ImportedName = "Imported preset";

    private readonly IPresetStoreFactory _stores;
    private readonly PresetManagerDialogs _dialogs;

    public PresetManagerActions(IPresetStoreFactory stores, PresetManagerDialogs dialogs) => (_stores, _dialogs) = (stores, dialogs);

    /// <summary>Renames one user preset; a replaced name is asked about first.</summary>
    public async Task<PresetActionResult> RenameAsync(PresetEntry entry, string text, IReadOnlyList<string> reserved)
    {
        if (_stores.StoreOf(entry.Node.Kind, entry.Node.Key) is not { } store) return new(false);
        var name = text.Trim();
        if (reserved.Contains(name, StringComparer.OrdinalIgnoreCase)) return new(false, ReservedText);
        var plan = PresetManagePresenter.PlanRename(entry.Name, text, store.Names());
        if (plan.Error is { } error) return new(false, error);
        if (plan.Unchanged) return new(true);
        if (plan.ReplacesName is { } clash && !await _dialogs.Confirm(string.Format(ReplaceQuestionFormat, clash))) return new(false);
        return Try(() => store.Rename(entry.Name, plan.Name, plan.ReplacesName is not null), plan.Name);
    }

    /// <summary>Copies one preset; a built-in drum map is copied from the shipped map, a built-in articulation, MIDI or effect preset from its bytes, other built-ins have no copy.</summary>
    public PresetActionResult Duplicate(PresetEntry entry, IReadOnlyList<string> names)
    {
        if (_stores.StoreOf(entry.Node.Kind, entry.Node.Key) is not { } store) return new(false);
        var copy = PresetRules.CopyName(entry.Name, names);
        if (entry.BuiltIn)
        {
            if (store is DrumMapStore maps) return Try(() => maps.DuplicateShipped(entry.Name, copy), copy);
            if (store is not IPresetRestorable restorable || PresetBuiltInCopy.BytesOf(entry.Node, entry.Name, _stores.Effects) is not { } bytes) return new(false);
            return Try(() => restorable.Restore(copy, bytes, int.MaxValue), copy);
        }
        return Try(() => store.Duplicate(entry.Name, copy), copy);
    }

    /// <summary>Deletes the chosen user presets at once (the built-in ones are skipped); the result carries the undo that puts them all back.</summary>
    public PresetActionResult Delete(IReadOnlyList<PresetEntry> chosen)
    {
        var doomed = chosen.Where(entry => !entry.BuiltIn).ToList();
        if (doomed.Count == 0) return new(false);
        var undos = new List<PresetDeleteUndo>();
        string? error = null;
        foreach (var group in doomed.GroupBy(entry => (entry.Node.Kind, entry.Node.Key)))
        {
            if (_stores.StoreOf(group.Key.Kind, group.Key.Key) is not { } store) continue;
            var result = Try(() => undos.Add(PresetDeleteUndo.Delete(store, group.Select(entry => entry.Name).ToList())), null);
            error ??= result.Error;
        }
        var undoable = undos.Where(undo => undo.CanUndo).ToList();
        return new(true, error, null, undoable.Count == 0 ? null : () => { foreach (var undo in undoable) undo.Undo(); });
    }

    /// <summary>Drops a user preset on another of the same place: it takes the other's position among the user's presets.</summary>
    public PresetActionResult Drop(PresetEntry entry, PresetEntry target)
    {
        if (entry.BuiltIn || target.BuiltIn || !entry.Node.Same(target.Node) || _stores.StoreOf(entry.Node.Kind, entry.Node.Key) is not { } store) return new(false);
        var to = store.Names().ToList().FindIndex(name => string.Equals(name, target.Name, StringComparison.OrdinalIgnoreCase));
        return to < 0 ? new(false) : Try(() => store.Move(entry.Name, to), entry.Name);
    }

    /// <summary>The file extension an exported preset of that node takes (".json"); null when the node has no files.</summary>
    public string? ExtensionOf(PresetNode node) => _stores.StoreOf(node.Kind, node.Key) is IPresetRestorable restorable ? restorable.FileExtension : null;

    /// <summary>The bytes of a preset to write to a file: the user's own, or the built-in one as it would be copied; null when there are none.</summary>
    public byte[]? ExportBytes(PresetEntry entry) =>
        entry.BuiltIn ? PresetBuiltInCopy.BytesOf(entry.Node, entry.Name, _stores.Effects)
            : _stores.StoreOf(entry.Node.Kind, entry.Node.Key) is IPresetRestorable restorable ? restorable.Snapshot(entry.Name) : null;

    /// <summary>Adds a preset from a file's bytes under the file's name (a copy name when taken or reserved); refuses bytes the kind cannot read.</summary>
    public PresetActionResult Import(PresetNode node, string fileName, byte[] bytes, IReadOnlyList<string> reserved)
    {
        if (_stores.StoreOf(node.Kind, node.Key) is not IPresetRestorable store) return new(false, NotImportable);
        if (!store.Accepts(bytes)) return new(false, string.Format(NotAPresetFormat, fileName));
        var name = Path.GetFileNameWithoutExtension(fileName).Trim();
        if (PresetRules.Error(name) is not null) name = ImportedName;
        var taken = store.Names().Concat(reserved).ToList();
        if (taken.Contains(name, StringComparer.OrdinalIgnoreCase)) name = PresetRules.CopyName(name, taken);
        return Try(() => store.Restore(name, bytes, int.MaxValue), name);
    }

    /// <summary>Moves the chosen user presets one row as a block, each place on its own; false when none can move.</summary>
    public PresetActionResult Move(IReadOnlyList<PresetEntry> chosen, int delta)
    {
        var moved = false;
        string? error = null;
        foreach (var group in chosen.Where(entry => !entry.BuiltIn).GroupBy(entry => (entry.Node.Kind, entry.Node.Key)))
        {
            if (_stores.StoreOf(group.Key.Kind, group.Key.Key) is not { } store) continue;
            var steps = PresetManagePresenter.BlockSteps(store.Names().ToList(), group.Select(entry => entry.Name).ToList(), delta);
            if (steps.Count == 0) continue;
            var result = Try(() => { foreach (var (name, index) in steps) store.Move(name, index); }, null);
            error ??= result.Error;
            moved |= result.Ok;
        }
        return new(moved, error);
    }

    /// <summary>True when some chosen user preset has an unchosen row beside it in its own store.</summary>
    public bool CanMove(IReadOnlyList<PresetEntry> chosen, int delta) =>
        chosen.Where(entry => !entry.BuiltIn).GroupBy(entry => (entry.Node.Kind, entry.Node.Key)).Any(group =>
            _stores.StoreOf(group.Key.Kind, group.Key.Key) is { } store
            && PresetManagePresenter.BlockSteps(store.Names().ToList(), group.Select(entry => entry.Name).ToList(), delta).Count > 0);

    private static PresetActionResult Try(Action change, string? select)
    {
        try { change(); return new(true, null, select); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException) { return new(false, ChangeFailed + ex.Message); }
    }
}
