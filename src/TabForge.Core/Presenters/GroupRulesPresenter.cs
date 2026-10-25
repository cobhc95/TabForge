// Owns: the Group rules editor's rules without UI types: the groups being edited (each with the name it had when the editor opened), the fallback
//       group's name, where the edit applies, the group and rule edits (add, move, delete, rename, colour, rule type and value), the default value of a
//       rule type, the rule value choices and hints, and the check of OK, which gives the result or the problem.
// Does not own: the window, the cards, the radio buttons and the colour menu (Views/GroupRulesDialog.cs), applying the result to a song (MixerRules.Apply,
//       called by MixerWindow), the rule matching (MixerRules).
// Tests: none dedicated(through the window), TestGroupRulesPresenter (headless).
using TabForge.Models;

namespace TabForge.Presenters;

/// <summary>Where the edited rules apply: every song (app settings), only the open song, or back to the app-wide rules.</summary>
public enum GroupRulesScope { AllSongs, ThisSong, ResetToApp }

/// <summary>The edited group rules: groups in priority order, the fallback group's name, old name -> new name for renamed groups, and where they apply.</summary>
public sealed record GroupRulesResult(List<MixerGroupDef> Groups, string Fallback, Dictionary<string, string> Renamed, GroupRulesScope Scope);

/// <summary>One group being edited: the name it had when the editor opened (null for a new group) and its working copy.</summary>
public sealed class GroupRulesEntry
{
    public string? Original { get; init; }
    public MixerGroupDef Def { get; init; } = new();
}

/// <summary>
/// Group rules editor state. Indexes are positions in <see cref="Groups"/> and in each group's rules; the view rebuilds after every structural change.
/// Thread: UI thread only (it is driven by the editor window).
/// </summary>
public sealed class GroupRulesPresenter
{
    private readonly MixerSettings _mixer;
    private readonly List<GroupRulesEntry> _groups = new();
    private readonly string _fallbackOriginal;

    /// <param name="mixer">The song's mixer settings; the editor opens on its groups and fallback and never changes them.</param>
    public GroupRulesPresenter(MixerSettings mixer)
    {
        _mixer = mixer;
        foreach (var g in MixerRules.Groups(mixer)) _groups.Add(new GroupRulesEntry { Original = g.Name, Def = g.Clone() });
        Fallback = MixerRules.FallbackOf(mixer);
        _fallbackOriginal = Fallback;
        AppliesToThisSongOnly = mixer.HasOwnRules;
    }

    /// <summary>The groups in priority order.</summary>
    public IReadOnlyList<GroupRulesEntry> Groups => _groups;

    /// <summary>The fallback group's name as typed (not trimmed).</summary>
    public string Fallback { get; set; }

    /// <summary>True when the result applies to this song only; false when it replaces the app-wide rules.</summary>
    public bool AppliesToThisSongOnly { get; set; }

    /// <summary>Adds a group named "New group" (or the next free "New group N") with a Piano family rule.</summary>
    public void AddGroup() => _groups.Add(new GroupRulesEntry
    {
        Def = new MixerGroupDef { Name = FreshName(), Rules = { new MixerRule(MixerRules.KindFamily, MixerRules.Piano) } },
    });

    /// <summary>Puts the defaults back. A default keeps a name the groups had when the editor opened, so its tracks follow the rename.</summary>
    public void ResetToDefaults()
    {
        var old = MixerRules.Groups(_mixer).Select(g => g.Name).ToList();
        _groups.Clear();
        foreach (var g in MixerRules.Defaults()) _groups.Add(new GroupRulesEntry { Original = old.Contains(g.Name) ? g.Name : null, Def = g });
        Fallback = MixerRules.DefaultFallback;
    }

    /// <summary>Moves the group at <paramref name="index"/> to position <paramref name="toIndex"/> in the priority order.</summary>
    public void MoveGroup(int index, int toIndex)
    {
        var entry = _groups[index];
        _groups.RemoveAt(index);
        _groups.Insert(toIndex, entry);
    }

    public void RemoveGroup(int index) => _groups.RemoveAt(index);

    public void SetGroupName(int index, string name) => _groups[index].Def.Name = name;

    public void SetGroupColour(int index, string hex) => _groups[index].Def.Colour = hex;

    /// <summary>Adds a Piano family rule to the group.</summary>
    public void AddRule(int index) => _groups[index].Def.Rules.Add(new MixerRule(MixerRules.KindFamily, MixerRules.Piano));

    public void RemoveRule(int index, int ruleIndex) => _groups[index].Def.Rules.RemoveAt(ruleIndex);

    public void SetRuleValue(int index, int ruleIndex, string value) => _groups[index].Def.Rules[ruleIndex].Value = value;

    /// <summary>Changes a rule's type; a new type gets its default value. False, and nothing changes, when the rule already has that type.</summary>
    public bool ChangeRuleKind(int index, int ruleIndex, string kind)
    {
        var rule = _groups[index].Def.Rules[ruleIndex];
        if (kind == rule.Kind) return false;
        rule.Kind = kind;
        rule.Value = DefaultValue(kind);
        return true;
    }

    /// <summary>
    /// The result of OK when every name can be used; otherwise null, and <paramref name="problem"/> says why. Names are trimmed in the result.
    /// Renamed groups map their old name to the new one (a group that kept its name maps to itself).
    /// </summary>
    public GroupRulesResult? TryBuild(out string problem)
    {
        var names = _groups.Select(e => e.Def.Name.Trim()).Append(Fallback.Trim()).ToList();
        problem = NameProblem(names);
        if (problem.Length > 0) return null;
        var renamed = new Dictionary<string, string>();
        foreach (var e in _groups.Where(e => e.Original is not null)) renamed[e.Original!] = e.Def.Name.Trim();
        if (Fallback.Trim() != _fallbackOriginal) renamed[_fallbackOriginal] = Fallback.Trim();
        var groups = _groups.Select(e => { var d = e.Def.Clone(); d.Name = d.Name.Trim(); return d; }).ToList();
        var scope = AppliesToThisSongOnly ? GroupRulesScope.ThisSong : GroupRulesScope.AllSongs;
        return new GroupRulesResult(groups, Fallback.Trim(), renamed, scope);
    }

    /// <summary>The result of "Reset to app rules": no groups, so the song drops its own rules and follows the app-wide ones.</summary>
    public static GroupRulesResult ResetToAppResult() => new(new(), "", new(), GroupRulesScope.ResetToApp);

    /// <summary>The choices a rule's value takes from a list (a family or a track kind), or null when the value is typed.</summary>
    public static string[]? ValueChoices(string kind) => kind switch
    {
        MixerRules.KindFamily => MixerRules.Families,
        MixerRules.KindTrack => MixerRules.TrackKinds,
        _ => null,
    };

    /// <summary>The tool tip of a typed rule value.</summary>
    public static string ValueHint(string kind) => kind switch
    {
        MixerRules.KindProgram => "A General MIDI program number 0-127, or a range such as 24-31",
        MixerRules.KindChannel => "A MIDI channel 1-16",
        _ => "Part of the track's name (not case sensitive)",
    };

    /// <summary>The line above a group's rules: what they do, or that the group has none.</summary>
    public static string RulesHint(int ruleCount) =>
        ruleCount == 0 ? "No rules: nothing goes here by rule (manual moves still can)." : "A track is in this group when ANY of these match:";

    private static string NameProblem(List<string> trimmedNames)
    {
        if (trimmedNames.Any(n => n.Length == 0)) return "Every group needs a name.";
        if (trimmedNames.Distinct(StringComparer.OrdinalIgnoreCase).Count() != trimmedNames.Count) return "Two groups have the same name.";
        if (trimmedNames.Any(n => n is MixerGroups.Audio or MixerGroups.Everything))
            return $"\"{MixerGroups.Audio}\" and \"{MixerGroups.Everything}\" are reserved names.";
        return "";
    }

    private string FreshName()
    {
        for (var i = 1; ; i++)
        {
            var name = i == 1 ? "New group" : $"New group {i}";
            if (_groups.All(e => e.Def.Name != name) && name != Fallback) return name;
        }
    }

    private static string DefaultValue(string kind) => kind switch
    {
        MixerRules.KindFamily => MixerRules.Piano,
        MixerRules.KindTrack => "Keys",
        MixerRules.KindProgram => "0-7",
        MixerRules.KindChannel => "1",
        _ => "",
    };
}
