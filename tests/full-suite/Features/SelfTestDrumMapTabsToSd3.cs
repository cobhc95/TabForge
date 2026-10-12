using System.Text.Json.Nodes;
using TabForge.Plugins;

namespace TabForge;

/// <summary>The shipped "Tabs to SD3" drum map and the built-in General MIDI preset that sends to it (part of <see cref="SelfTest"/>).</summary>
public static partial class SelfTest
{
    private static void TestDrumMapTabsToSd3()
    {
        var map = DrumMapLibrary.Find("Tabs to SD3");
        Check("the Tabs to SD3 drum map ships with the app and is marked unverified", map is { IsUser: false, Verified: false }, map?.Name ?? "missing");
        var table = DrumMapLibrary.BuildTable("General MIDI", "Tabs to SD3", "");
        Check("General MIDI to Tabs to SD3: 40 sends 38, and 38, 36 and 42 stay on their note",
            table[40] == 38 && table[38] == 38 && table[36] == 36 && table[42] == 42, $"40->{table[40]} 38->{table[38]} 36->{table[36]} 42->{table[42]}");
        var changed = Enumerable.Range(0, 128).Where(i => table[i] != i).ToArray();
        Check("General MIDI to Tabs to SD3 is not the identity", changed.Length > 0 && changed.Contains(40), string.Join(",", changed));
        Check("a note the map does not name passes through unchanged", table[60] == 60 && table[0] == 0 && table[127] == 127, $"60->{table[60]}");

        var preset = MidiProcessorCatalog.BuiltInPresets
            .First(p => p.Name.StartsWith("Drums: GM", StringComparison.Ordinal) && p.Name.EndsWith("humanize", StringComparison.Ordinal)).Build();
        Check("the GM drum preset is note map, humanizer, then velocity x1.3",
            preset.Select(p => p.Type).SequenceEqual(new[] { "noteMap", "humanize", "velocity" }) && JsonNode.Parse(preset[2].Params)!["Mul"]!.GetValue<double>() == 1.3,
            string.Join(",", preset.Select(p => p.Type)));
        var engineMap = JsonNode.Parse(MidiProcessorCatalog.EngineParams(preset[0]))!["Map"]!.AsArray();
        Check("the GM drum preset's engine table sends 40 to 38", engineMap[40]!.GetValue<int>() == 38 && engineMap[38]!.GetValue<int>() == 38,
            $"40->{engineMap[40]} 38->{engineMap[38]}");
    }
}
