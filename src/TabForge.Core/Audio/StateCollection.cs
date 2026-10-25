using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Audio;

// Owns: the per-plug-in result of reading plug-in states before a save, and the save warning it makes.
// Does not own: reading the states (AudioEngineClient.CollectStates).
// Tests: TestSaveFlowCharacterization.

/// <summary>What happened to one plug-in's state when states were collected before a save.</summary>
public enum PluginStateOutcome { Captured, Unchanged, TimedOut, Failed, TooLarge }

public sealed record PluginStateResult(TrackModel Track, PluginSlot Plugin, PluginStateOutcome Outcome);

/// <summary>Per-plug-in result of reading the plug-in states before a save.</summary>
public sealed class StateCollection
{
    public List<PluginStateResult> Results { get; } = new();
    /// <summary>Wall-clock time spent collecting (ms, diagnostics).</summary>
    public double ElapsedMs { get; set; }
    /// <summary>Plug-ins whose current state could not be read (timed out, failed, too large): the song keeps their previous settings.</summary>
    public int IncompleteCount => Results.Count(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge);
    public bool Complete => IncompleteCount == 0;
    /// <summary>The save warning, or null when every state was read.</summary>
    public string? Warning => Complete ? null
        : $"{IncompleteCount} plug-in state{(IncompleteCount == 1 ? "" : "s")} could not be read ({string.Join(", ", Results.Where(r => r.Outcome is PluginStateOutcome.TimedOut or PluginStateOutcome.Failed or PluginStateOutcome.TooLarge).Select(r => $"{(r.Plugin.Name.Length > 0 ? r.Plugin.Name : Path.GetFileNameWithoutExtension(r.Plugin.Path))}: {Describe(r.Outcome)}").Distinct().Take(4))}); the saved song has their previous settings";

    private static string Describe(PluginStateOutcome outcome) => outcome switch
    {
        PluginStateOutcome.TimedOut => "no answer",
        PluginStateOutcome.TooLarge => $"larger than {PluginStateLimits.MaxRawBytes / 1048576} MiB",
        _ => "failed",
    };
}
