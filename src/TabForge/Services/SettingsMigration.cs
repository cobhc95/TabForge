using System.IO;
using System.Text;
using System.Text.Json;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: normalising settings files written by older versions.
// Does not own: validation of values and file access.
// Tests: CheckDocumentContextLeftovers, TestTrimMerges.
/// <summary>Normalizes current and legacy settings documents without discarding unknown supported fields.</summary>
public static class SettingsMigration
{
    /// <summary>D6: fills only missing (null) sections and collections with defaults; never changes a value that is present. Used by load and by the validator.</summary>
    public static void EnsureSections(AppSettings settings)
    {
        settings.Tabs ??= new TabSettings();
        settings.General ??= new GeneralSettings();
        settings.Appearance ??= new AppearanceSettings();
        settings.Audio ??= new AudioSettings();
        settings.Editing ??= new EditingSettings();
        settings.Follow ??= new FollowSettings();
        settings.Timeline ??= new TimelineSettings();
        settings.LiveVideo ??= new Video.LiveVideoSettings();
        settings.Learn ??= new KeyboardMode.KeyboardModeSettings();
        settings.Hotkeys ??= new HotkeySettings();
        settings.Hotkeys.Bindings ??= new Dictionary<string, string>();
        settings.Hotkeys.DisabledActions ??= new List<string>();
        settings.Hotkeys.Bindings2 ??= new Dictionary<string, string>();
        settings.Hotkeys.DisabledActions2 ??= new List<string>();
        settings.Appearance.RecentColours ??= new List<string>();
    }

    public static AppSettings Normalize(string json, AppSettings settings)
    {
        if (Encoding.UTF8.GetByteCount(json) > InputLimits.MaxSettingsJsonBytes)
            throw new InvalidDataException("The settings data exceeds the 2 MiB size limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = InputLimits.MaxJsonDepth });
        var root = document.RootElement;
        EnsureSections(settings);

        // Plug-in states kept inline by older builds (auto-load chains, startup tracks) move to their own files.
        if (settings.Plugins is { } plugins)
        {
            try
            {
                foreach (var chain in plugins.AutoChains ?? new()) TabForge.Plugins.ChainStateStore.Externalise(chain.Plugins ?? new());
                foreach (var track in plugins.StartupTracks ?? new()) TabForge.Plugins.ChainStateStore.Externalise(track.Plugins ?? new());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Services.Trace.Error(Services.Trace.Engine, "settings migration: externalise plug-in state: " + ex.Message); /* keep inline; retried next load */ }
        }

        // Files written before the second hotkey slot load as Hotkey 1 only.
        if (!Has(Find(root, "Hotkeys"), "Bindings2") && !Has(Find(root, "Hotkeys"), "DisabledActions2"))
        {
            DisableSecondSlotWhereFirstWasChanged(settings.Hotkeys, HotkeyCatalog.All);
            KeepUserKeysOverNewDefaults(settings.Hotkeys, HotkeyCatalog.All);
        }

        if (!Has(root, "Editing"))
        {
            settings.Editing.LeftHanded = settings.LeftHanded;
            settings.Editing.ShowNoteNames = settings.ShowNoteNames;
            settings.Editing.PreviewHorizon = settings.PreviewHorizon;
            settings.Editing.PreviewNotesEnabled = settings.PreviewHorizon > 0;
            settings.Editing.ScaleHighlight = settings.ScaleHighlight;
            settings.Editing.FretboardFrets = settings.FretboardFrets;
        }
        else
        {
            var editing = Find(root, "Editing");
            if (!Has(editing, "LeftHanded")) settings.Editing.LeftHanded = settings.LeftHanded;
            if (!Has(editing, "ShowNoteNames")) settings.Editing.ShowNoteNames = settings.ShowNoteNames;
            if (!Has(editing, "PreviewHorizon")) settings.Editing.PreviewHorizon = settings.PreviewHorizon;
            if (!Has(editing, "PreviewNotesEnabled")) settings.Editing.PreviewNotesEnabled = settings.Editing.PreviewHorizon > 0;
            if (!Has(editing, "ScaleHighlight")) settings.Editing.ScaleHighlight = settings.ScaleHighlight;
            if (!Has(editing, "FretboardFrets")) settings.Editing.FretboardFrets = settings.FretboardFrets;
        }

        if (!Has(root, "Audio"))
        {
            settings.Audio.Metronome = settings.Metronome;
            settings.Audio.CountIn = settings.CountIn;
            settings.Audio.Speed = settings.Speed;
            settings.Audio.PreviewNotes = settings.PreviewNotes;
        }

        if (!Has(root, "Appearance"))
        {
            settings.Appearance.ScorePaper = settings.DarkPaper ? "Dark" : "Light";
            settings.Appearance.ShowFretboard = settings.ShowInstrument;
            settings.Appearance.ShowArrangementOverview = settings.ShowArrangement;
        }

        if (!Has(root, "Follow"))
            settings.Follow.Mode = settings.General.AutoScroll ? FollowModes.Smooth : FollowModes.Off;
        else
        {
            var follow = Find(root, "Follow");
            if (!Has(follow, "PlayheadColour") && Has(Find(root, "Appearance"), "PlayheadColour"))
                settings.Follow.PlayheadColour = settings.Appearance.PlayheadColour;
        }

        // Files written before "match the instrument" existed stored the fixed fretboard default: move them on once.
        if (!Has(Find(root, "Editing"), "InstrumentViewVersion"))
        {
            settings.Editing.InstrumentView = InstrumentViews.MatchInstrument;
            settings.Editing.InstrumentViewVersion = 1;
        }

        // The fretboard size lock used to default to locked: a stored lock in an older file is that default, so unlock once.
        // A stored unlocked value stays; a lock set again afterwards is saved with the version and kept.
        if (!Has(Find(root, "Appearance"), "InstrumentLockVersion"))
        {
            settings.Appearance.LockInstrumentSize = false;
            settings.Appearance.InstrumentLockVersion = 1;
        }

        // Ledger lines no longer have an opacity of their own (they share the staff lines' colour and opacity). The old
        // Appearance.LedgerLineOpacity / LedgerOpacityVersion keys are no longer properties: loading ignores them and the next save
        // leaves them out. The staff-line opacity is kept exactly as stored. Nothing needs to run, so nothing can run twice.

        // The Windows MIDI latency default was a guessed 60 ms; the Windows synth measured about 200 ms. A stored 60 in an older file is the old
        // default (moved once); any other stored value is the user's own choice and stays.
        if (!Has(Find(root, "Plugins"), "WindowsMidiLatencyVersion"))
        {
            if (settings.Plugins.WindowsMidiLatencyMs == 60) settings.Plugins.WindowsMidiLatencyMs = 200;
            settings.Plugins.WindowsMidiLatencyVersion = 1;
        }

        if (!Has(root, "Tabs")) settings.Tabs = new TabSettings();
        if (settings.Tabs is null) settings.Tabs = new TabSettings();
        settings.Appearance.RecentColours = settings.Appearance.RecentColours
            .Where(IsColour).Distinct(StringComparer.OrdinalIgnoreCase).Take(InputLimits.MaxRecentColours).ToList();

        var defaults = SettingsCatalog.Build(new AppSettings()).ToDictionary(item => item.Key, StringComparer.OrdinalIgnoreCase);
        foreach (var descriptor in SettingsCatalog.Build(settings))
        {
            if (descriptor.Kind == SettingKind.Number)
            {
                var value = Convert.ToDouble(descriptor.Get());
                descriptor.Set(double.IsFinite(value) ? Math.Clamp(value, descriptor.Min, descriptor.Max) : descriptor.Min);
            }
            else if (descriptor.Kind == SettingKind.Choice &&
                     !descriptor.Choices.Contains(descriptor.Get()?.ToString() ?? "", StringComparer.OrdinalIgnoreCase) &&
                     defaults.TryGetValue(descriptor.Key, out var fallback))
            {
                descriptor.Set(fallback.Get());
            }
            else if (descriptor.Kind == SettingKind.Colour && !SettingsColor.IsValid(descriptor.Get()?.ToString()) &&
                     defaults.TryGetValue(descriptor.Key, out var defaultColour))
            {
                descriptor.Set(defaultColour.Get());
            }
        }
        return SettingsValidator.Normalize(settings);
    }

    public static AppSettings Clone(AppSettings settings) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(settings)) ?? new AppSettings();

    private static bool IsColour(string value)
    {
        return SettingsColor.IsValid(value);
    }

    /// <summary>
    /// An old file that customised or cleared Hotkey 1 of an action with a default Hotkey 2 gets that Hotkey 2 cleared,
    /// so a default never appears behind the user's back.
    /// </summary>
    internal static void DisableSecondSlotWhereFirstWasChanged(HotkeySettings hotkeys, IEnumerable<HotkeyAction> actions)
    {
        foreach (var action in actions)
        {
            if (string.IsNullOrWhiteSpace(action.DefaultGesture2)) continue;
            if (hotkeys.IsDisabled(action.Id) || !string.IsNullOrWhiteSpace(hotkeys[action.Id]))
                hotkeys.Disable(action.Id, 2);
        }
    }

    /// <summary>
    /// An old file's own binding is never overridden by a default added later: an untouched slot whose default key is a key
    /// the file already binds to another command is cleared. Only commands with a default Hotkey 2 are checked (their keys are the new ones).
    /// </summary>
    internal static void KeepUserKeysOverNewDefaults(HotkeySettings hotkeys, IEnumerable<HotkeyAction> actions)
    {
        var taken = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in hotkeys.Bindings.Concat(hotkeys.Bindings2))
            if (!string.IsNullOrWhiteSpace(pair.Value)) taken[HotkeyCatalog.Canonical(pair.Value)] = pair.Key;
        foreach (var action in actions)
            foreach (var slot in string.IsNullOrWhiteSpace(action.DefaultGesture2) ? Array.Empty<int>() : new[] { 1, 2 })   // only commands that have a default Hotkey 2: their keys are the new ones
            {
                var gesture = HotkeyCatalog.DefaultFor(action, slot);
                if (string.IsNullOrWhiteSpace(gesture) || hotkeys.IsDisabled(action.Id, slot) || !string.IsNullOrWhiteSpace(hotkeys.Get(action.Id, slot))) continue;
                if (taken.TryGetValue(HotkeyCatalog.Canonical(gesture), out var owner) && !owner.Equals(action.Id, StringComparison.OrdinalIgnoreCase))
                    hotkeys.Disable(action.Id, slot);
            }
    }

    private static JsonElement Find(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object) return default;
        foreach (var property in element.EnumerateObject())
            if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return default;
    }

    private static bool Has(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && Find(element, name).ValueKind != JsonValueKind.Undefined;
}
