using System.IO;
using TabForge.Audio.Contracts;

namespace TabForge.Rendering;

/// <summary>
/// Bookkeeping for "is every chain the render needs loaded, at the generation that was requested?". Pure state (no engine, no
/// clock): required slots with the generation that must be acknowledged, acknowledgements as they arrive (a newer generation
/// also satisfies an older requirement), and the resulting ready / missing / problem lists.
/// </summary>
public sealed class ChainReadiness
{
    private readonly Dictionary<int, int> _required = new();
    private readonly Dictionary<int, ChainAck> _acks = new();

    public ChainReadiness(IEnumerable<(int Slot, int Generation)> required)
    {
        foreach (var (slot, generation) in required) _required[slot] = Math.Max(generation, _required.GetValueOrDefault(slot));
    }

    /// <summary>Records an acknowledgement; one older than what is already known for the slot is ignored.</summary>
    public void Acknowledge(ChainAck ack)
    {
        if (_acks.TryGetValue(ack.Slot, out var known) && known.Generation > ack.Generation) return;
        _acks[ack.Slot] = ack;
    }

    private bool Satisfied(int slot, int generation) => _acks.TryGetValue(slot, out var ack) && ack.Generation >= generation;

    /// <summary>Every required slot has reported its required generation (or a later one).</summary>
    public bool IsReady => _required.All(kv => Satisfied(kv.Key, kv.Value));

    /// <summary>Required slots that have not yet reported their generation, ascending.</summary>
    public IReadOnlyList<int> MissingSlots => _required.Where(kv => !Satisfied(kv.Key, kv.Value)).Select(kv => kv.Key).OrderBy(s => s).ToList();

    /// <summary>Required slots that reported and are up to date.</summary>
    public IReadOnlyList<int> ReadySlots => _required.Where(kv => Satisfied(kv.Key, kv.Value)).Select(kv => kv.Key).OrderBy(s => s).ToList();

    /// <summary>Plug-ins of up-to-date required chains that are not loaded (failed, quarantined, blocked).</summary>
    public IReadOnlyList<(int Slot, PluginLoadResult Plugin)> Problems
    {
        get
        {
            var list = new List<(int, PluginLoadResult)>();
            foreach (var (slot, generation) in _required.OrderBy(kv => kv.Key))
                if (Satisfied(slot, generation))
                    foreach (var p in _acks[slot].Plugins)
                        if (p.Status != PluginLoadStatus.Loaded) list.Add((slot, p));
            return list;
        }
    }

    /// <summary>Human-readable "name (why)" entries of <see cref="Problems"/>, without repeats.</summary>
    public IReadOnlyList<string> ProblemDescriptions => Problems
        .Select(x => $"{Path.GetFileNameWithoutExtension(x.Plugin.Path)} ({Describe(x.Plugin.Status)})").Distinct().ToList();

    public static string Describe(PluginLoadStatus status) => status switch
    {
        PluginLoadStatus.Failed => "failed to load",
        PluginLoadStatus.SkippedQuarantined => "quarantined after a crash",
        PluginLoadStatus.BlockedUntrusted => "blocked, not trusted",
        PluginLoadStatus.BlockedChanged => "blocked, changed since you approved it",
        _ => "loaded",
    };
}

/// <summary>Pure naming for a render job's own staging files and the publish step.</summary>
public static class RenderStaging
{
    /// <summary>".name.tfrender-{jobId}.tmp.wav" next to the final file (same volume for the rename; the real extension stays last for encoders).</summary>
    public static string TempName(string finalFileName, string jobId)
        => "." + Path.GetFileNameWithoutExtension(finalFileName) + ".tfrender-" + jobId + ".tmp" + Path.GetExtension(finalFileName);

    public static string TempPath(string finalPath, string jobId)
        => Path.Combine(Path.GetDirectoryName(finalPath) ?? "", TempName(Path.GetFileName(finalPath), jobId));

    /// <summary>True for a temp file this job created (never a planned final path, never another job's file).</summary>
    public static bool IsOwnedTemp(string path, string jobId)
        => Path.GetFileName(path) is var n && n.StartsWith(".", StringComparison.Ordinal) && n.Contains(".tfrender-" + jobId + ".tmp", StringComparison.Ordinal);

    /// <summary>
    /// Publishes a finished temp file: an atomic rename to <paramref name="finalPath"/> that never overwrites; when the target
    /// appeared meanwhile, the next free "name (2).ext" is used. Returns the path actually published.
    /// </summary>
    public static string Publish(string tempPath, string finalPath)
    {
        var dir = Path.GetDirectoryName(finalPath) ?? "";
        var name = Path.GetFileName(finalPath);
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var target = Path.Combine(dir, Services.RenderNaming.Unique(dir, name));
            try { File.Move(tempPath, target, overwrite: false); return target; }
            catch (IOException) when (File.Exists(target)) { /* lost the race: pick the next free name */ } // Not logged: name collision: the next free name is tried.
        }
        throw new IOException("Could not find a free file name for " + name);
    }
}
