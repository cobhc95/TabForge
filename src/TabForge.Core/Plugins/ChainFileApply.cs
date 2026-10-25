using System.Collections.Generic;
using System.IO;
using TabForge.Services;

namespace TabForge.Plugins;

/// <summary>What a chain file load did: whether the chain changed, the index of the first plug-in it added, and the status-line sentence.</summary>
public readonly record struct ChainLoadOutcome(bool Applied, int FirstAdded, string Notice);

// Owns: putting a saved FX chain file (.tfchain) onto a chain list, either replacing the list or adding to its end, with the
//   plug-in count limit and the sentences the status line shows for each outcome.
// Does not own: reading and validating the file (PluginLibrary.LoadChain), the file dialogs and the chain rows (Avalonia FxChainFiles and
//   FxChainWindow), plug-in trust (PluginTrust decides when the engine loads the chain).
// Tests: TabForge.Avalonia.Tests.FxChainWindowTests (loading a chain file through the window).
public static class ChainFileApply
{
    public const string FileExtension = ".tfchain";

    /// <summary>The checked read of a chain: what the status line says, and the plug-ins to put on the chain (null unless the outcome is applied).</summary>
    public readonly record struct ChainFileRead(ChainLoadOutcome Outcome, List<PluginSlot>? Plugins);

    /// <summary>
    /// Reads <paramref name="path"/> and checks it against the chain it would join (<paramref name="currentCount"/> plug-ins): the file must be a chain with plug-ins
    /// and, when added to the end, fit the plug-in limit. Throws FileNotFoundException when the file is gone, and ArgumentException for a path without the chain extension.
    /// </summary>
    public static ChainFileRead Read(string path, int currentCount, bool replace) =>
        Check(Path.GetFileName(path), PluginLibrary.LoadChain(FilePathPolicy.ExistingFile(path, "FX chain", FileExtension)), currentCount, replace);

    /// <summary>
    /// The same checks for a chain that came from elsewhere (an imported chain file): <paramref name="loaded"/> is null when it was not a valid chain;
    /// <paramref name="noneFound"/> is the sentence for a file whose plug-ins were all missing (default: the file has no plug-ins).
    /// </summary>
    public static ChainFileRead Check(string name, List<PluginSlot>? loaded, int currentCount, bool replace, string? noneFound = null)
    {
        if (loaded is null) return new(new ChainLoadOutcome(false, -1, $"{name} is not a valid FX chain."), null);
        if (loaded.Count == 0) return new(new ChainLoadOutcome(false, -1, noneFound ?? $"{name} has no plug-ins."), null);
        if (!replace && currentCount + loaded.Count > InputLimits.MaxPluginsPerTrack)
            return new(new ChainLoadOutcome(false, -1, $"{name} would make this chain longer than {InputLimits.MaxPluginsPerTrack} plug-ins."), null);
        return new(new ChainLoadOutcome(true, replace ? 0 : currentCount, replace ? $"Loaded {name}." : $"Added {name} to the end."), loaded);
    }

    /// <summary>
    /// Loads <paramref name="path"/> into <paramref name="chain"/>. A file outside the limits or with no plug-ins leaves the list as it was.
    /// Throws FileNotFoundException when the file is gone, and ArgumentException for a path without the chain extension.
    /// </summary>
    public static ChainLoadOutcome Apply(string path, List<PluginSlot> chain, bool replace)
    {
        var read = Read(path, chain.Count, replace);
        if (read.Plugins is not { } loaded) return read.Outcome;
        if (replace) chain.Clear();
        chain.AddRange(loaded);
        return read.Outcome;
    }
}
