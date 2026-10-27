using System.IO;
using System.Text;
using System.Text.Json;

namespace TabForge.Services;

// Owns: normalising settings files written by older versions.
// Does not own: validation of values and file access.
// Tests: CheckDocumentContextLeftovers.
/// <summary>Normalizes current and legacy settings documents without discarding unknown supported fields.</summary>
public static class SettingsMigration
{
    /// <summary>Fills only missing (null) sections and collections with defaults; never changes a value that is present. Used by load and by the validator.</summary>
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

    /// <summary>Moves an older settings document to the current shape, one named step per change, then bounds every value.</summary>
    public static AppSettings Normalize(string json, AppSettings settings)
    {
        if (Encoding.UTF8.GetByteCount(json) > InputLimits.MaxSettingsJsonBytes)
            throw new InvalidDataException("The settings data exceeds the 2 MiB size limit.");
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = InputLimits.MaxJsonDepth });
        var root = document.RootElement;
        EnsureSections(settings);
        ExternaliseInlinePluginStates(settings);
        MigrateHotkeySlots(root, settings);
        MigrateLegacySections(root, settings);
        MigrateVersionedDefaults(root, settings);
        ClampCatalogValues(root, settings);
        return SettingsValidator.Normalize(settings);
    }

    private static void ExternaliseInlinePluginStates(AppSettings settings)
    {
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
    }

    private static void MigrateHotkeySlots(JsonElement root, AppSettings settings)
    {
        // Files written before the second hotkey slot load as Hotkey 1 only.
        if (!Has(Find(root, "Hotkeys"), "Bindings2") && !Has(Find(root, "Hotkeys"), "DisabledActions2"))
        {
            DisableSecondSlotWhereFirstWasChanged(settings.Hotkeys, HotkeyCatalog.All);
            KeepUserKeysOverNewDefaults(settings.Hotkeys, HotkeyCatalog.All);
        }
    }

    /// <summary>Files written before the Editing, Audio, Appearance and Follow sections kept those values at the top level.</summary>
    private static void MigrateLegacySections(JsonElement root, AppSettings settings)
    {
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

        // The old "Off" mode is now the Follow playback switch: a file that stored it turns following off and keeps the default mode.
        if (string.Equals(settings.Follow.Mode, "Off", StringComparison.OrdinalIgnoreCase))
        {
            settings.Follow.FollowPlayback = false;
            settings.Follow.Mode = new FollowSettings().Mode;
        }

        if (!Has(root, "Appearance"))
        {
            settings.Appearance.ScorePaper = settings.DarkPaper ? "Dark" : "Light";
            settings.Appearance.ShowFretboard = settings.ShowInstrument;
            settings.Appearance.ShowArrangementOverview = settings.ShowArrangement;
        }

        if (!Has(root, "Follow"))
            settings.Follow.FollowPlayback = settings.General.AutoScroll;
        else
        {
            var follow = Find(root, "Follow");
            if (!Has(follow, "PlayheadColour") && Has(Find(root, "Appearance"), "PlayheadColour"))
                settings.Follow.PlayheadColour = settings.Appearance.PlayheadColour;
            // The "Playback line colour" row is gone (Appearance > Playback highlight colours the line): a colour the user changed moves there once.
            var lineDefault = new FollowSettings().PlayheadColour;
            if (Has(follow, "PlayheadColour") && IsLegacyAuto(settings.Appearance.PlaybackHighlight)
                && !string.Equals(settings.Follow.PlayheadColour, lineDefault, StringComparison.OrdinalIgnoreCase))
            {
                settings.Appearance.PlaybackHighlight = PlaybackHighlightModes.Custom;
                settings.Appearance.PlaybackHighlightColour = settings.Follow.PlayheadColour;
                settings.Follow.PlayheadColour = lineDefault;
            }
        }

        MigratePlaybackHighlightAuto(root, settings);
    }

    // The "Auto" playback highlight is gone. A stored Auto becomes Follow accent, except when the Follow page's old "Playing note colour" was changed from its
    // default: that colour was the one in effect under Auto (the only place a user could have written one), so it moves into Custom and is kept.
    // Under Custom the custom colour wins and the old note colour is dropped; under Follow accent the accent wins. Either way the retired value is reset to its default.
    private static void MigratePlaybackHighlightAuto(JsonElement root, AppSettings settings)
    {
        var appearance = settings.Appearance;
        var noteDefault = new FollowSettings().HighlightColour;
        var noteChanged = Has(Find(root, "Follow"), "HighlightColour") && !string.Equals(settings.Follow.HighlightColour, noteDefault, StringComparison.OrdinalIgnoreCase) && IsColour(settings.Follow.HighlightColour);
        if (IsLegacyAuto(appearance.PlaybackHighlight))
        {
            if (noteChanged) { appearance.PlaybackHighlight = PlaybackHighlightModes.Custom; appearance.PlaybackHighlightColour = settings.Follow.HighlightColour; }
            else appearance.PlaybackHighlight = PlaybackHighlightModes.FollowAccent;
        }
        settings.Follow.HighlightColour = noteDefault;
    }

    private static bool IsLegacyAuto(string? mode) => string.Equals(mode, "Auto", StringComparison.OrdinalIgnoreCase);

    /// <summary>Defaults that changed: a version key in the file says the change already ran, so each runs once.</summary>
    private static void MigrateVersionedDefaults(JsonElement root, AppSettings settings)
    {
        // Files written before "match the instrument" existed stored the fixed fretboard default: move them on once.
        if (!Has(Find(root, "Editing"), "InstrumentViewVersion"))
        {
            settings.Editing.InstrumentView = InstrumentViews.MatchInstrument;
            settings.Editing.InstrumentViewVersion = 1;
        }

        // "Move to the next note after writing" now defaults off: a file from before the default (no marker) goes back to keeping the cursor on
        // the note just written once. The marker is then saved, so an opt-in the user sets afterwards is kept.
        if (!Has(Find(root, "Editing"), "MoveToNextNoteVersion") || settings.Editing.MoveToNextNoteVersion < 1)
        {
            settings.Editing.MoveToNextNoteAfterWriting = false;
            settings.Editing.MoveToNextNoteVersion = 1;
        }

        // A file without InstrumentLockVersion stored the locked fretboard size as its default: unlock once.
        // A stored unlocked value stays; a lock set again afterwards is saved with the version and kept.
        if (!Has(Find(root, "Appearance"), "InstrumentLockVersion"))
        {
            settings.Appearance.LockInstrumentSize = false;
            settings.Appearance.InstrumentLockVersion = 1;
        }

        // Compact became the default spacing: a file without DensityVersion holding Comfortable moves on once; a choice made afterwards is kept.
        if (!Has(Find(root, "Appearance"), "DensityVersion"))
        {
            if (string.Equals(settings.Appearance.Density, "Comfortable", StringComparison.OrdinalIgnoreCase)) settings.Appearance.Density = "Compact";
            settings.Appearance.DensityVersion = 1;
        }

        // Ledger lines share the staff lines' colour and opacity. Appearance.LedgerLineOpacity / LedgerOpacityVersion keys in a file
        // are not properties: loading ignores them and the next save leaves them out. Nothing needs to run here.

        // The playing-bar band became subtle and on by default: a file without PlayingBarVersion stored the old defaults (off, 20%). The old default values
        // move on once (there was no "user set it" marker); any other stored opacity is the user's own and stays, and the version is saved with the file.
        if (!Has(Find(root, "Follow"), "PlayingBarVersion"))
        {
            settings.Follow.PlayingBarEnabled = true;
            if (Math.Abs(settings.Follow.PlayingBarOpacity - 0.20) < 0.0001) settings.Follow.PlayingBarOpacity = FollowSettings.DefaultPlayingBarOpacity;
            settings.Follow.PlayingBarVersion = 2;
        }
        // Version 2 lowered the default from 10% to 5%: a stored 10% from version 1 was that default and moves on once; other values are the user's.
        else if (settings.Follow.PlayingBarVersion < 2)
        {
            if (Math.Abs(settings.Follow.PlayingBarOpacity - 0.10) < 0.0001) settings.Follow.PlayingBarOpacity = FollowSettings.DefaultPlayingBarOpacity;
            settings.Follow.PlayingBarVersion = 2;
        }

        // The Windows MIDI latency default was a guessed 60 ms; the Windows synth measured about 200 ms. A stored 60 in an older file is the old
        // default (moved once); any other stored value is the user's own choice and stays.
        if (!Has(Find(root, "Plugins"), "WindowsMidiLatencyVersion"))
        {
            if (settings.Plugins.WindowsMidiLatencyMs == 60) settings.Plugins.WindowsMidiLatencyMs = 200;
            settings.Plugins.WindowsMidiLatencyVersion = 1;
        }

        // Remembering the plug-in list became the default: a file without RememberScanVersion stored the old off default.
        if (!Has(Find(root, "Plugins"), "RememberScanVersion")) { settings.Plugins.RememberScan = true; settings.Plugins.RememberScanVersion = 1; }

        // The single BufferSize / SampleRate of an older file belong to its current output: keep them as that output's profile, so leaving it and coming back finds them.
        if (!Has(Find(root, "Plugins"), "OutputProfilesVersion"))
        {
            var plugins = settings.Plugins;
            plugins.OutputProfiles ??= new();
            plugins.OutputProfiles.TryAdd(PluginSettings.OutputKey(plugins.Driver, plugins.Device), new OutputProfile { BufferSize = plugins.BufferSize, SampleRate = plugins.SampleRate });
            plugins.OutputProfilesVersion = 1;
        }
    }

    private static void ClampCatalogValues(JsonElement root, AppSettings settings)
    {
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
