using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Audio;

// Owns: asking the engine what each loaded plug-in of a track's chain reports about itself (ids, pin counts, names).
// Does not own: plug-in states (CollectStatesAsync), what is done with the answer (RfxChainExporter).
// Tests: no dedicated test class.
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

    /// <summary>Longer than a plug-in in its own process takes to describe itself (one second), which the engine waits for before it answers the next one.</summary>
    internal const int DefaultPerPluginMs = 1500;
    private int _nextInfoRequest;   // its own counter: an info reply is only matched in _infoRequests, never against a state request id
    private readonly Dictionary<int, InfoRequest> _infoRequests = new();   // guarded by _gate

    /// <summary>
    /// One entry per plug-in of <paramref name="plugins"/> (default: the track's chain as it is now), in order: what the loaded plug-in reports,
    /// <see cref="LoadedPluginInfo.Unavailable"/> when the engine says it has none loaded, or null when there is no answer (engine not running,
    /// an engine that does not know the request, or the plug-in did not answer within <paramref name="perPluginMs"/> of the one before, or in that time for each plug-in of the chain).
    /// The engine answers in chain order on its main thread, so a slow plug-in holds back the ones after it: what has no reply is asked for once more.
    /// Never throws for a missing answer.
    /// </summary>
    public async Task<LoadedPluginInfo?[]> GetPluginInfoAsync(TrackModel track, IReadOnlyList<TabForge.Plugins.PluginSlot>? plugins = null, int perPluginMs = DefaultPerPluginMs)
    {
        plugins ??= track.Rig.Plugins.ToList();
        if (!IsRunning || !_slots.TryGetValue(track, out var slot)) return new LoadedPluginInfo?[plugins.Count];
        return await CollectInfoAsync(plugins.Count, () => AskPluginInfoAsync(slot, plugins, perPluginMs));
    }

    /// <summary>Asks up to twice and keeps the first reply of each plug-in (a chain that changed under the first ask answers in full the second time).</summary>
    internal static async Task<LoadedPluginInfo?[]> CollectInfoAsync(int count, Func<Task<LoadedPluginInfo?[]>> askOnce)
    {
        var result = new LoadedPluginInfo?[count];
        for (var attempt = 0; attempt < 2 && result.Any(r => r is null); attempt++)
        {
            var answer = await askOnce();
            for (var i = 0; i < count; i++) result[i] ??= answer[i];
        }
        return result;
    }

    private async Task<LoadedPluginInfo?[]> AskPluginInfoAsync(int slot, IReadOnlyList<TabForge.Plugins.PluginSlot> plugins, int perPluginMs)
    {
        var result = new LoadedPluginInfo?[plugins.Count];
        var id = BeginInfoRequest(slot, out var request);
        Send(EngineCommand.GetPluginInfo, new GetPluginInfoMessage(slot, id).Write);
        await WaitForRepliesAsync(request, plugins.Count, perPluginMs);
        lock (_gate) _infoRequests.Remove(id);
        lock (request)
        {
            if (request.Mismatch) return result;
            for (var position = 0; position < plugins.Count; position++)
            {
                // As for states: the answer's index is the place in the chain the engine runs; the plug-in is found there by its id.
                var i = request.PluginIds is { } ids ? Array.IndexOf(ids, plugins[position].Id) : position;
                if (i >= 0 && request.Replies.TryGetValue(i, out var info)) result[position] = info;
                else if (request.Expected == 0) result[position] = LoadedPluginInfo.Unavailable;   // the engine has no chain for this track
            }
        }
        return result;
    }

    /// <summary>Ends when all replied, when no new reply came within <paramref name="perPluginMs"/>, or after <paramref name="perPluginMs"/> for each of <paramref name="count"/> plug-ins.</summary>
    internal static async Task WaitForRepliesAsync(InfoRequest request, int count, int perPluginMs)
    {
        var budget = Task.Delay(perPluginMs * Math.Max(1, count));
        for (var seen = 0; await Task.WhenAny(request.Done.Task, Task.Delay(perPluginMs), budget) is var ended && ended != request.Done.Task && ended != budget;)
        {
            int now; lock (request) now = request.Replies.Count;
            if (now == seen) break;
            seen = now;
        }
    }

    internal int BeginInfoRequest(int slot, out InfoRequest request)
    {
        var id = Interlocked.Increment(ref _nextInfoRequest);
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
