namespace TabForge.Audio.Contracts;

/// <summary>What became of one plug-in of a requested chain. BlockedChanged: approved, but the file changed before the engine loaded it.</summary>
public enum PluginLoadStatus : byte { Loaded = 0, Failed = 1, SkippedQuarantined = 2, BlockedUntrusted = 3, BlockedChanged = 4 }

/// <summary>One plug-in's result (Index -1: the General MIDI synth).</summary>
public sealed record PluginLoadResult(int Index, PluginLoadStatus Status, string Path);

/// <summary>
/// The engine's acknowledgement of a chain request: the slot, the generation of the request it answers (per-slot count of
/// LoadChain requests since the engine started; the client counts the same way) and every plug-in's outcome.
/// </summary>
public sealed record ChainAck(int Slot, int Generation, IReadOnlyList<PluginLoadResult> Plugins);
