using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine;

// Owns: answering EngineCommand.GetPluginInfo (what each loaded plug-in of a chain reports about itself).
// Does not own: plug-in states (EngineSession.SendStates), reading the description itself (Vst3ClassInfo).
// Tests: TestRfxChainExport (the wire record and the client's handling of the answer).
internal sealed partial class EngineSession
{
    /// <summary>
    /// One <see cref="EngineEvent.PluginInfo"/> frame per plug-in of the chain, bypassed ones too; a plug-in that is not loaded in this
    /// process answers "unavailable". Main thread only; each read carries the plug-in breadcrumb like every main-thread plug-in call.
    /// </summary>
    public void SendPluginInfo(int slot, int requestId)
    {
        AssertMain();
        void Reply(int index, int count, LoadedPluginInfo info) =>
            Send(EngineEvent.PluginInfo, w => { w.Write(slot); w.Write(requestId); w.Write(index); w.Write(count); info.Write(w); });

        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || request.Specs.Count == 0)
        {
            Reply(-1, 0, LoadedPluginInfo.Unavailable);
            return;
        }
        var count = request.Specs.Count;
        for (var index = 0; index < count; index++)
        {
            var spec = request.Specs[index];
            var plugin = chain.ById.GetValueOrDefault(spec.Id)
                ?? (Parked.TryGetValue(slot, out var parked) ? parked.GetValueOrDefault(spec.Id) : null)
                ?? (spec.Id.Length == 0 ? chain.Plugins.FirstOrDefault(p => p.Path == spec.Path) : null);
            var info = LoadedPluginInfo.Unavailable;
            if (plugin is Vst2Plugin or Vst3Plugin)
            {
                var entered = EnterCall(slot, index, plugin, spec.Path, PluginCallKind.Other);
                try { info = Vst3ClassInfo.Describe(plugin); }
                catch (Exception ex) { EngineLog.Write($"plug-in info failed: {ex.Message}"); }
                finally { LeaveCall(entered); }
            }
            Reply(index, count, info);
        }
    }
}
