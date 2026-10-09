using System.Text.Json.Serialization;

namespace TabForge.Models;

/// <summary>One condition of a mixer group: a track matches the group when any of the group's rules match.</summary>
public sealed class MixerRule
{
    /// <summary>One of <see cref="MixerRules.RuleKinds"/>.</summary>
    public string Kind { get; set; } = MixerRules.KindFamily;
    /// <summary>Family name, program number or range ("24-31"), track kind, MIDI channel (1-16) or name text, by <see cref="Kind"/>.</summary>
    public string Value { get; set; } = "";
    public MixerRule() { }
    public MixerRule(string kind, string value) { Kind = kind; Value = value; }
}

/// <summary>A mixer group of the "By instrument" grouping: its name, optional colour and rules (any of them matching puts a track in it).</summary>
public sealed class MixerGroupDef
{
    public string Name { get; set; } = "";
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Colour { get; set; }
    public List<MixerRule> Rules { get; set; } = new();
    public MixerGroupDef Clone() => new() { Name = Name, Colour = Colour, Rules = Rules.Select(r => new MixerRule(r.Kind, r.Value)).ToList() };
}

/// <summary>The app-wide group rules (from the app settings): songs without their own rules use these. Null groups / fallback = the defaults.</summary>
public sealed class MixerAppRules
{
    public List<MixerGroupDef>? Groups { get; set; }
    public string? Fallback { get; set; }
}

// Owns: which mixer group a track belongs to by rule (families, programs, kinds, channels, names), the defaults, and applying edited rules to a song.
// Does not own: manual per-track group choices (TrackModel.MixerGroup, applied in MixerGroups.GroupOf), the editor window (GroupRulesDialog).
// Tests: TestMixerGroupRules.
public static class MixerRules
{
    public const string KindFamily = "Instrument family", KindProgram = "Instrument (GM program)", KindTrack = "Track kind",
        KindChannel = "MIDI channel", KindName = "Name contains";
    public static readonly string[] RuleKinds = { KindFamily, KindProgram, KindTrack, KindChannel, KindName };

    public const string Guitar = "Guitar", Bass = "Bass", Drums = "Drums", Piano = "Piano", Keys = "Organ & chromatic percussion",
        Strings = "Strings & ensemble", Vocals = "Vocals", Brass = "Brass", Reeds = "Reeds & pipes", Synth = "Synth lead & pad", Sounds = "Other sounds";
    public static readonly string[] Families = { Guitar, Bass, Drums, Piano, Keys, Strings, Vocals, Brass, Reeds, Synth, Sounds };
    public static readonly string[] TrackKinds = { "Guitar", "Bass", "Drums", "Keys", "Other", "Audio" };
    public const string DefaultFallback = "Other instruments";

    /// <summary>Guitars, Basses, Drums; everything else falls to <see cref="DefaultFallback"/>.</summary>
    public static List<MixerGroupDef> Defaults() => new()
    {
        new() { Name = MixerGroups.Guitars, Rules = { new(KindFamily, Guitar) } },
        new() { Name = MixerGroups.Basses, Rules = { new(KindFamily, Bass) } },
        new() { Name = MixerGroups.Drums, Rules = { new(KindFamily, Drums) } },
    };

    /// <summary>The instrument family of a track, from its kind, MIDI channel and GM program (never its name).</summary>
    public static string FamilyOf(TrackModel track)
    {
        if (track.Kind == TrackKind.Drums || track.MidiChannel == 9) return Drums;
        var p = track.MidiProgram;
        if (track.Kind == TrackKind.Bass || p is >= 32 and <= 39) return Bass;
        return p switch
        {
            >= 24 and <= 31 => Guitar,
            <= 7 => Piano,
            <= 23 => Keys,
            >= 52 and <= 54 or 85 => Vocals,
            >= 40 and <= 55 => Strings,
            >= 56 and <= 63 => Brass,
            >= 64 and <= 79 => Reeds,
            >= 80 and <= 95 => Synth,
            _ => Sounds,
        };
    }

    public static bool Matches(MixerRule rule, TrackModel track)
    {
        var v = (rule.Value ?? "").Trim();
        switch (rule.Kind)
        {
            case KindFamily: return !track.IsAudio && string.Equals(FamilyOf(track), v, StringComparison.OrdinalIgnoreCase);
            case KindProgram:
                if (track.IsAudio || track.Kind == TrackKind.Drums || track.MidiChannel == 9) return false;
                var parts = v.Split('-', 2, StringSplitOptions.TrimEntries);
                if (!int.TryParse(parts[0], out var lo)) return false;
                var hi = parts.Length == 2 && int.TryParse(parts[1], out var h) ? h : lo;
                return track.MidiProgram >= Math.Min(lo, hi) && track.MidiProgram <= Math.Max(lo, hi);
            case KindTrack: return string.Equals(track.Kind.ToString(), v, StringComparison.OrdinalIgnoreCase);
            case KindChannel: return int.TryParse(v, out var ch) && track.MidiChannel + 1 == ch;
            case KindName: return v.Length > 0 && (track.Name ?? "").Contains(v, StringComparison.OrdinalIgnoreCase);
            default: return false;
        }
    }

    /// <summary>The first group (in list order) with a matching rule, else the fallback group; audio tracks without a matching rule are in "Audio".</summary>
    public static string Classify(MixerSettings mixer, TrackModel track)
    {
        foreach (var group in Groups(mixer))
            if (group.Rules.Any(r => Matches(r, track))) return group.Name;
        return track.IsAudio ? MixerGroups.Audio : FallbackOf(mixer);
    }

    /// <summary>The groups in effect: the song's own rules, else the app-wide rules, else the defaults.</summary>
    public static IReadOnlyList<MixerGroupDef> Groups(MixerSettings mixer) => mixer.Rules ?? mixer.App?.Groups ?? Defaults();
    public static string FallbackOf(MixerSettings mixer)
    {
        var name = mixer.Rules is not null ? mixer.Fallback : mixer.App?.Fallback;
        return string.IsNullOrWhiteSpace(name) ? DefaultFallback : name;
    }

    /// <summary>Every group name in display order (rule groups, then the fallback).</summary>
    public static List<string> Names(MixerSettings mixer) =>
        Groups(mixer).Select(g => g.Name).Append(FallbackOf(mixer)).Distinct(StringComparer.Ordinal).ToList();

    private static bool SameAs(IReadOnlyList<MixerGroupDef> a, IReadOnlyList<MixerGroupDef> b) =>
        a.Count == b.Count && a.Zip(b, (x, y) => x.Name == y.Name && x.Colour == y.Colour && x.Rules.Count == y.Rules.Count
            && x.Rules.Zip(y.Rules, (r, q) => r.Kind == q.Kind && r.Value == q.Value).All(t => t)).All(t => t);

    /// <summary>Old group name -> new name: levels, buses, collapsed state and manual choices follow a rename.</summary>
    public static void ApplyRenames(SongProject project, IReadOnlyDictionary<string, string>? renamed)
    {
        var mixer = project.Mixer;
        foreach (var (from, to) in renamed ?? new Dictionary<string, string>())
        {
            if (from == to) continue;
            if (mixer.Groups.Remove(from, out var levels)) mixer.Groups[to] = levels;
            if (mixer.Buses.Remove(from, out var bus)) mixer.Buses[to] = bus;
            if (mixer.CollapsedGroups.Remove(from)) mixer.CollapsedGroups.Add(to);
            foreach (var t in project.Tracks.Where(t => t.MixerGroup == from)) t.MixerGroup = to;
        }
    }

    /// <summary>A manual choice naming a group that no longer exists is dropped (the rules place the track).</summary>
    public static void DropStaleChoices(SongProject project)
    {
        var names = MixerGroups.AllNames(project.Mixer).ToHashSet();
        foreach (var t in project.Tracks.Where(t => t.MixerGroup is not null && !names.Contains(t.MixerGroup))) t.MixerGroup = null;
    }

    /// <summary>
    /// Sets this song's own rules (renames carry over). Rules equal to the app-wide ones are stored as "no own rules", so the song keeps following the app.
    /// </summary>
    public static void Apply(SongProject project, List<MixerGroupDef> groups, string fallback, IReadOnlyDictionary<string, string>? renamed = null)
    {
        var mixer = project.Mixer;
        ApplyRenames(project, renamed);
        var follows = fallback == (mixer.App?.Fallback ?? DefaultFallback) && SameAs(groups, mixer.App?.Groups ?? Defaults());
        mixer.Rules = follows ? null : groups.Select(g => g.Clone()).ToList();
        mixer.Fallback = follows || fallback == DefaultFallback ? null : fallback;
        DropStaleChoices(project);
    }

    /// <summary>Sets the app-wide rules (the caller saves the settings) and makes this song follow them (renames carry over).</summary>
    public static void ApplyAppWide(MixerAppRules app, SongProject project, List<MixerGroupDef> groups, string fallback, IReadOnlyDictionary<string, string>? renamed = null)
    {
        var isDefault = fallback == DefaultFallback && SameAs(groups, Defaults());
        app.Groups = isDefault ? null : groups.Select(g => g.Clone()).ToList();
        app.Fallback = fallback == DefaultFallback ? null : fallback;
        project.Mixer.App = app;
        ApplyRenames(project, renamed);
        project.Mixer.Rules = null; project.Mixer.Fallback = null;
        DropStaleChoices(project);
    }

    /// <summary>"Reset to app rules": the song drops its own rules and follows the app-wide ones.</summary>
    public static void ResetToApp(SongProject project)
    {
        project.Mixer.Rules = null; project.Mixer.Fallback = null;
        DropStaleChoices(project);
    }
}
