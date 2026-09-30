using System.IO;

namespace TabForge.Audio.Contracts;

/// <summary>
/// Chain loading in several frames: <see cref="EngineCommand.LoadChain"/> carries the chain spec without state blobs (plus a load id and the
/// indices whose state follows), then one <see cref="EngineCommand.ChainState"/> frame per plug-in, then <see cref="EngineCommand.ChainCommit"/>.
/// Each state fits one frame (<see cref="PluginStateLimits"/>), so a chain's states together may exceed <see cref="Frames.MaxFrameBytes"/>.
/// </summary>
public static class ChainLoadProtocol
{
    /// <summary>Sends one chain load through <paramref name="send"/> (one call per frame). A state over the contract is not sent (the engine reports it missing).</summary>
    public static void Send(Action<EngineCommand, Action<BinaryWriter>> send, int slot, bool useSynth, string track, IReadOnlyList<PluginSpec> specs, int loadId)
    {
        var withState = new List<int>();
        for (var i = 0; i < specs.Count; i++) if (specs[i].State is { Length: > 0 }) withState.Add(i);
        var bare = specs.Select(s => s with { State = null }).ToList();
        send(EngineCommand.LoadChain, w =>
        {
            w.Write(slot); w.Write(useSynth); w.WriteString(track); w.Write(bare);
            w.Write(loadId); w.Write(withState.Count); foreach (var i in withState) w.Write(i);
        });
        foreach (var i in withState)
        {
            var spec = specs[i];
            if (spec.State!.Length > PluginStateLimits.MaxBase64Chars) continue;
            send(EngineCommand.ChainState, w => { w.Write(slot); w.Write(loadId); w.Write(i); w.WriteString(spec.Id); w.WriteString(spec.State); });
        }
        send(EngineCommand.ChainCommit, w => { w.Write(slot); w.Write(loadId); });
    }

    /// <summary>A chain load whose states are still arriving.</summary>
    public sealed class Pending
    {
        public required int Slot { get; init; }
        public required int LoadId { get; init; }
        public required string Track { get; init; }
        public required bool UseSynth { get; init; }
        public required List<PluginSpec> Specs { get; init; }
        public required HashSet<int> Expected { get; init; }
        public Dictionary<int, string> States { get; } = new();
    }

    /// <summary>A committed chain: specs with their states filled in, and the indices whose announced state never arrived (they load with defaults).</summary>
    public sealed record Ready(int Slot, string Track, bool UseSynth, List<PluginSpec> Specs, List<int> Missing);

    /// <summary>Reader-thread assembly of chain loads, one pending load per slot (a newer load supersedes an unfinished one).</summary>
    public sealed class Assembler
    {
        private readonly Dictionary<int, Pending> _pending = new();

        public void OnLoadChain(BinaryReader r)
        {
            var slot = r.ReadInt32(); var useSynth = r.ReadBoolean(); var track = r.ReadBoundedString(256); var specs = r.ReadChain();
            var loadId = r.ReadInt32(); var count = r.ReadInt32();
            if (count < 0 || count > specs.Count) throw new InvalidDataException("Bad state count.");
            var expected = new HashSet<int>();
            for (var i = 0; i < count; i++) { var index = r.ReadInt32(); if (index >= 0 && index < specs.Count) expected.Add(index); }
            _pending[slot] = new Pending { Slot = slot, LoadId = loadId, Track = track, UseSynth = useSynth, Specs = specs, Expected = expected };
        }

        public void OnChainState(BinaryReader r)
        {
            var slot = r.ReadInt32(); var loadId = r.ReadInt32(); var index = r.ReadInt32(); var id = r.ReadBoundedString(64);
            var state = r.ReadBoundedString(PluginStateLimits.MaxBase64Chars);
            if (!_pending.TryGetValue(slot, out var p) || p.LoadId != loadId || !p.Expected.Contains(index) || p.Specs[index].Id != id) return;   // stale / unknown
            p.States[index] = state;
        }

        /// <summary>Null for a commit that does not match the slot's pending load.</summary>
        public Ready? OnCommit(BinaryReader r)
        {
            var slot = r.ReadInt32(); var loadId = r.ReadInt32();
            if (!_pending.TryGetValue(slot, out var p) || p.LoadId != loadId) return null;
            _pending.Remove(slot);
            var specs = new List<PluginSpec>(p.Specs.Count);
            var missing = new List<int>();
            for (var i = 0; i < p.Specs.Count; i++)
            {
                if (p.States.TryGetValue(i, out var s)) specs.Add(p.Specs[i] with { State = s });
                else { specs.Add(p.Specs[i]); if (p.Expected.Contains(i)) missing.Add(i); }
            }
            return new Ready(slot, p.Track, p.UseSynth, specs, missing);
        }
    }
}
