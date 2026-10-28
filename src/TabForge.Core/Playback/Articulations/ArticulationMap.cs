namespace TabForge.Playback;

// Owns: the articulation map of one instrument: its rules by technique id and the keyswitch lead, and how it is kept in a plug-in slot
//   (ArticulationBindings: technique id -> encoded rule; the lead under the key "@LeadMs"), so songs saved without it load unchanged.
// Does not own: what a rule is (ArticulationRule), the presets (ArticulationPresets) or the compile hook (ScoreToMidiCompiler.Articulations).
// Tests: TestArticulationKeyswitches.

public sealed class ArticulationMap
{
    public const string LeadKey = "@LeadMs";
    public const int DefaultLeadMs = 10, MaxLeadMs = 200, MaxRules = 64;

    /// <summary>How long before the note the keyswitch or controller is sent.</summary>
    public int LeadMs { get; set; } = DefaultLeadMs;
    public Dictionary<string, ArticulationRule> Rules { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool IsEmpty => Rules.Count == 0;

    /// <summary>The map kept in a slot; malformed or unknown entries are left out, so a hand-edited file never breaks playback.</summary>
    public static ArticulationMap FromBindings(IReadOnlyDictionary<string, string>? bindings)
    {
        var map = new ArticulationMap();
        if (bindings is null) return map;
        KeyValuePair<string, string>[] pairs;
        try { pairs = bindings.ToArray(); }
        catch (InvalidOperationException) { return map; }   // a dictionary changed by the editor while the compile reads it
        foreach (var (key, value) in pairs)
        {
            if (key == LeadKey) { if (int.TryParse(value, out var lead)) map.LeadMs = Math.Clamp(lead, 0, MaxLeadMs); }
            else if (map.Rules.Count < MaxRules && ArticulationTechniques.Find(key) is { } row && ArticulationRule.TryParse(value, out var rule)) map.Rules[row.Id] = rule;
        }
        return map;
    }

    /// <summary>The stored form: a new dictionary, empty when there are no rules (the lead alone is not kept).</summary>
    public Dictionary<string, string> ToBindings()
    {
        var bindings = new Dictionary<string, string>();
        if (IsEmpty) return bindings;
        bindings[LeadKey] = Math.Clamp(LeadMs, 0, MaxLeadMs).ToString(System.Globalization.CultureInfo.InvariantCulture);
        foreach (var (id, rule) in Rules) bindings[id] = rule.Encode();
        return bindings;
    }
}
