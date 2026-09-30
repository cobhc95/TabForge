using System.IO;
using System.Text;
using System.Text.Json;
using TabForge.Documents;

namespace TabForge.Services;

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
        settings.Hotkeys ??= new HotkeySettings();
        settings.Hotkeys.Bindings ??= new Dictionary<string, string>();
        settings.Hotkeys.DisabledActions ??= new List<string>();
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* keep inline; retried next load */ }
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

        // The Windows MIDI latency default was a guessed 60 ms; the Windows synth measured about 200 ms. A stored 60 in an older file is the old
        // default (moved once); any other stored value is the user's own choice and stays.
        if (!Has(Find(root, "Plugins"), "WindowsMidiLatencyVersion"))
        {
            if (settings.Plugins.WindowsMidiLatencyMs == 60) settings.Plugins.WindowsMidiLatencyMs = 200;
            settings.Plugins.WindowsMidiLatencyVersion = 1;
        }

        if (!Has(root, "Tabs")) settings.Tabs = new TabSettings();
        if (settings.Tabs is null) settings.Tabs = new TabSettings();
        settings.Appearance.UiScale = Math.Clamp(settings.Appearance.UiScale, 0.8, 1.5);
        settings.Appearance.AnimationSpeed = Math.Clamp(settings.Appearance.AnimationSpeed, 0.25, 2);
        settings.Follow.VerticalTriggerPercent = Math.Clamp(settings.Follow.VerticalTriggerPercent, 40, 95);
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
