using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: asking the engine what each loaded plug-in of a track's chain reports about itself (ids, pin counts, names).
// Does not own: plug-in states (CollectStatesAsync), what is done with the answer (RfxChainExporter).
// Tests: TestRfxChainExport.
public sealed partial class AudioEngineClient
{
    internal sealed class InfoRequest
    {
        public int Slot { get; init; }
        public string[]? PluginIds { get; init; }
        public int Expected = -1;
        /// <summary>The engine's chain is not the chain that was asked about: every answer is dropped.</summary>
        public bool Mismatch;
        public readonly Dictionary<int, LoadedPluginInfo> Replies = new();
        public readonly TaskCompletionSource<bool> Done = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private readonly Dictionary<int, InfoRequest> _infoRequests = new();   // guarded by _gate

    /// <summary>
    /// One entry per plug-in of <paramref name="plugins"/> (default: the track's chain as it is now), in order: what the loaded plug-in reports, or null when that is not
    /// available (engine not running, plug-in not loaded or running in its own process, an engine that does not know the request,
    /// or no answer within <paramref name="timeoutMs"/>). Never throws for a missing answer.
    /// </summary>
    public async Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<TabForge.Plugins.PluginSlot>? plugins = null, int timeoutMs = 2000)
    {
        plugins ??= track.Rig.Plugins.ToList();
        var result = new LoadedPluginInfo?[plugins.Count];
        if (!IsRunning || !_slots.TryGetValue(track, out var slot)) return result;
        var id = BeginInfoRequest(slot, out var request);
        Send(EngineCommand.GetPluginInfo, new GetPluginInfoMessage(slot, id).Write);
        await Task.WhenAny(request.Done.Task, Task.Delay(timeoutMs));
        lock (_gate) _infoRequests.Remove(id);
        lock (request)
        {
            if (request.Mismatch) return result;
            for (var position = 0; position < plugins.Count; position++)
            {
                // As for states: the answer's index is the place in the chain the engine runs; the plug-in is found there by its id.
                var i = request.PluginIds is { } ids ? Array.IndexOf(ids, plugins[position].Id) : position;
                if (i >= 0 && request.Replies.TryGetValue(i, out var info) && info.Format != LoadedPluginInfo.None) result[position] = info;
            }
        }
        return result;
    }

    internal int BeginInfoRequest(int slot, out InfoRequest request)
    {
        var id = Interlocked.Increment(ref _nextStateRequest);
        request = new InfoRequest { Slot = slot, PluginIds = _sentChainIds.GetValueOrDefault(slot) };
        lock (_gate) _infoRequests[id] = request;
        return id;
    }

    /// <summary>Reader thread: one <see cref="EngineEvent.PluginInfo"/> frame (bounded read; late or unknown answers are dropped).</summary>
    internal void OnPluginInfo(BinaryReader r)
    {
        var slot = r.ReadInt32(); var requestId = r.ReadInt32(); var index = r.ReadInt32();
        var count = Math.Clamp(r.ReadInt32(), 0, InputLimits.MaxPluginsPerTrack);
        var info = LoadedPluginInfo.Read(r);
        InfoRequest? request;
        lock (_gate) if (!_infoRequests.TryGetValue(requestId, out request) || request.Slot != slot) return;
        lock (request)
        {
            request.Expected = count;
            // A chain of another length than the one asked about (it was reloaded meanwhile): its indexes name other plug-ins.
            if (request.PluginIds is { } ids && ids.Length != count) { request.Mismatch = true; request.Replies.Clear(); request.Done.TrySetResult(false); return; }
            if (index >= 0 && index < count) request.Replies.TryAdd(index, info);
            if (request.Replies.Count >= request.Expected) request.Done.TrySetResult(true);
        }
    }
}
