using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TabForge.Plugins;
using TabForge.Presenters;

// Owns: the four changes the preset manager can make (rename, duplicate, delete, move), each through an IPresetStore and the shared Core
//   rules (PresetManagePresenter, PresetRules, PresetDeleteQuestionPresenter), and one question for a whole delete.
// Does not own: the list and its selection (PresetManagerWindow), the files' rules (PresetFolderStore, DrumMapStore) or the question box
//   (PresetManagerDialogs).
// Tests: PresetManagerWindowTests.
namespace TabForge.Avalonia.Views.Presets;

/// <summary>What a change did: whether it was made, why not, and the name to select afterwards.</summary>
public sealed record PresetActionResult(bool Ok, string? Error = null, string? Select = null);

/// <summary>The manager's changes over the injected stores, with the questions asked through the injected dialogs.</summary>
public sealed class PresetManagerActions
{
    private const string ReservedText = "A preset that comes with the app has this name. Choose another name.";
    private const string ReplaceQuestionFormat = "A preset named \"{0}\" already exists. Replace it?";
    private const string ChangeFailed = "The preset could not be changed: ";

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

    /// <summary>Copies one preset; a built-in drum map is copied from the shipped map, other built-ins have no copy.</summary>
    public PresetActionResult Duplicate(PresetEntry entry, IReadOnlyList<string> names)
    {
        if (_stores.StoreOf(entry.Node.Kind, entry.Node.Key) is not { } store) return new(false);
        var copy = PresetRules.CopyName(entry.Name, names);
        if (entry.BuiltIn)
        {
            if (store is not DrumMapStore maps) return new(false);
            return Try(() => maps.DuplicateShipped(entry.Name, copy), copy);
        }
        return Try(() => store.Duplicate(entry.Name, copy), copy);
    }

    /// <summary>Deletes the chosen user presets after one question for all of them; false when the user stops it.</summary>
    public async Task<PresetActionResult> DeleteAsync(IReadOnlyList<PresetEntry> chosen)
    {
        var doomed = chosen.Where(entry => !entry.BuiltIn).ToList();
        if (doomed.Count == 0 || !await PresetDeleteQuestion.ConfirmAsync(doomed.Select(entry => entry.Name).ToList(),
            _dialogs.AskDelete, _dialogs.ConfirmDeletePresets, _dialogs.RememberDeletePresets)) return new(false);
        string? error = null;
        foreach (var group in doomed.GroupBy(entry => (entry.Node.Kind, entry.Node.Key)))
        {
            if (_stores.StoreOf(group.Key.Kind, group.Key.Key) is not { } store) continue;
            var result = Try(() => { foreach (var entry in group) store.Delete(entry.Name); }, null);
            error ??= result.Error;
        }
        return new(true, error);
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
