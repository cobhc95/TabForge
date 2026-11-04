using TabForge.Services;

namespace TabForge.Presenters;

// Owns: the minimal user tuning-preset rules (no storage of its own): the checks of a typed
//     name and tuning, and saving, renaming and deleting in a caller-owned list; the thin settings
//     adapter (entries in AppSettings) that loads, converts and writes the list back.
// Does not own: the settings file (a save callback writes it), the built-in catalog
//     (TuningPresets), the menu entries (TuningPresetMenu) or any window.
// Tests: TabForge.Avalonia.Tests.TuningPresetCatalogTests.
// Threads: the caller owns the list; like the draft, use on one thread.
public sealed record TuningUserPreset(string Name, int[] HighToLow);

/// <summary>
/// Coherent minimal management for user tuning presets without touching the settings file:
/// the caller keeps a <see cref="List{T}"/> (later a settings field) and these rules guard it.
/// </summary>
public static class TuningUserRules
{
    public const int MaxPresets = 24;

    /// <summary>Why the name and tuning cannot be saved; null when they can. <paramref name="ignoreName"/> is the preset being replaced, so saving over it is allowed.</summary>
    public static string? Error(string name, IReadOnlyList<int>? highToLow, IEnumerable<TuningUserPreset> existing, string? ignoreName = null)
    {
        if (PresetRules.Error(name) is { } nameError) return nameError;
        if (highToLow is null) return "Pick a tuning first.";
        if (highToLow.Count is < TrackPropertiesPresenter.FewestStrings or > TrackPropertiesPresenter.MostStrings)
            return $"A tuning has {TrackPropertiesPresenter.FewestStrings}–{TrackPropertiesPresenter.MostStrings} strings.";
        if (highToLow.Any(m => m is < TrackPropertiesPresenter.LowestNote or > TrackPropertiesPresenter.HighestNote))
            return $"Each string is {TrackPropertiesPresenter.LowestNote}–{TrackPropertiesPresenter.HighestNote}.";
        var trimmed = name.Trim();
        if (TuningPresets.Catalog.Any(p => string.Equals(p.Preset.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return "A preset that comes with the app has that name.";
        if (existing.Any(p => !string.Equals(p.Name, ignoreName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase)))
            return "A preset with that name already exists.";
        return null;
    }

    /// <summary>Saves a user preset (replaces the same name); false with the reason when it cannot be saved.</summary>
    public static bool TrySave(List<TuningUserPreset> store, string name, IEnumerable<int> highToLow, out string? error)
    {
        var tuning = highToLow.ToArray();
        error = Error(name, tuning, store, ignoreName: name.Trim());
        if (error is not null) return false;
        if (!store.Any(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) && store.Count >= MaxPresets)
        {
            error = "There is no room for another tuning preset.";
            return false;
        }
        var entry = new TuningUserPreset(name.Trim(), tuning);
        var at = store.FindIndex(p => string.Equals(p.Name, entry.Name, StringComparison.OrdinalIgnoreCase));
        if (at >= 0) store[at] = entry;
        else store.Add(entry);
        error = null;
        return true;
    }

    /// <summary>Renames a user preset; false with the reason when it cannot be renamed.</summary>
    public static bool TryRename(List<TuningUserPreset> store, string from, string to, out string? error)
    {
        var current = store.FirstOrDefault(p => string.Equals(p.Name, from.Trim(), StringComparison.OrdinalIgnoreCase));
        if (current is null) { error = "The preset is not there any more."; return false; }
        error = Error(to, current.HighToLow, store, ignoreName: current.Name);
        if (error is not null) return false;
        store[store.IndexOf(current)] = current with { Name = to.Trim() };
        error = null;
        return true;
    }

    /// <summary>Deletes a user preset; true when one was removed.</summary>
    public static bool Delete(List<TuningUserPreset> store, string name) =>
        store.RemoveAll(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)) > 0;

    public static IReadOnlyList<string> Names(IEnumerable<TuningUserPreset> store) => store.Select(p => p.Name).ToList();
}

/// <summary>The settings adapter for the person's tuning presets: the entries in <see cref="Services.AppSettings"/>
/// as caller-owned <see cref="TuningUserPreset"/> lists and back. No static mutable state: every call converts anew.</summary>
public static class TuningUserSettings
{
    /// <summary>The entries the settings file holds for <paramref name="users"/> (trimmed names, copied tunings).</summary>
    public static List<Services.TuningUserPresetEntry> ToEntries(IEnumerable<TuningUserPreset> users) =>
        (users ?? Enumerable.Empty<TuningUserPreset>()).Where(u => u is not null && u.Name is not null && u.HighToLow is not null)
            .Select(u => new Services.TuningUserPresetEntry { Name = u.Name.Trim(), HighToLow = u.HighToLow.ToList() }).ToList();

    /// <summary>The caller-owned presets for the stored <paramref name="entries"/>; null rows are skipped, tunings are copied.</summary>
    public static List<TuningUserPreset> FromEntries(IEnumerable<Services.TuningUserPresetEntry>? entries)
    {
        var list = new List<TuningUserPreset>();
        if (entries is null) return list;
        foreach (var entry in entries)
        {
            if (entry is null || entry.Name is null || entry.HighToLow is null) continue;
            list.Add(new TuningUserPreset(entry.Name, entry.HighToLow.ToArray()));
        }
        return list;
    }

    /// <summary>The caller-owned presets in <paramref name="settings"/> (empty when none were saved yet).</summary>
    public static List<TuningUserPreset> LoadUserEntries(Services.AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return FromEntries(settings.TuningUserPresets);
    }
}

/// <summary>The person's tuning presets in one <see cref="Services.AppSettings"/> with the save callback run after each change.
/// The settings object owns the list; this store converts through <see cref="TuningUserSettings"/> and never keeps static state.</summary>
public sealed class TuningUserSettingsStore
{
    private readonly Services.AppSettings _settings;
    private readonly Action? _save;

    /// <param name="settings">The settings whose TuningUserPresets holds the presets.</param>
    /// <param name="save">Writes the settings after each change; null keeps them in memory.</param>
    public TuningUserSettingsStore(Services.AppSettings settings, Action? save = null)
    {
        _settings = settings;
        _save = save;
    }

    /// <summary>The person's presets now (a fresh list; mutate through this store so the settings follow).</summary>
    public List<TuningUserPreset> Users => TuningUserSettings.LoadUserEntries(_settings);

    /// <summary>Saves a preset and the settings; false with the reason when it cannot be saved.</summary>
    public bool TrySave(string name, IEnumerable<int> highToLow, out string? error)
    {
        var users = Users;
        if (!TuningUserRules.TrySave(users, name, highToLow, out error)) return false;
        _settings.TuningUserPresets = TuningUserSettings.ToEntries(users);
        _save?.Invoke();
        return true;
    }

    /// <summary>Renames a preset and the settings; false with the reason when it cannot be renamed.</summary>
    public bool TryRename(string from, string to, out string? error)
    {
        var users = Users;
        if (!TuningUserRules.TryRename(users, from, to, out error)) return false;
        _settings.TuningUserPresets = TuningUserSettings.ToEntries(users);
        _save?.Invoke();
        return true;
    }

    /// <summary>Deletes a preset and the settings; true when one was removed.</summary>
    public bool Delete(string name)
    {
        var users = Users;
        if (!TuningUserRules.Delete(users, name)) return false;
        _settings.TuningUserPresets = TuningUserSettings.ToEntries(users);
        _save?.Invoke();
        return true;
    }

    public IReadOnlyList<string> Names() => TuningUserRules.Names(Users);
}
