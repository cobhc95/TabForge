using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Plugins;

namespace TabForge.Audio;

// Owns: the engine side of the "Active on" rule: the one live bypass message per plug-in, and re-matching it when the output changes.
// Does not own: the rule and matcher (Plugins/ActiveOn/ActiveOnRule.cs), the chain sync that loads the chains (AudioEngineClient.Sync.cs).
// Tests: FxActiveOnTests.
// Threads: the client's sync and UI threads only; the audio thread reads the resulting Bypass flag as before.

/// <summary>Keeps the engine's live bypass in step with the chain switches, the containers and the "Active on" rules.</summary>
public sealed partial class AudioEngineClient
{
    /// <summary>Every plug-in's live bypass: its own switch, its container's, its Active on rule, and all of them while the chain is off (they stay loaded). Sent per plug-in that changed.</summary>
    private void SyncBypass(TrackModel track, int slot)
    {
        var chainOn = track.SoundSource == SoundSources.Plugins;
        ChainTree.Refresh(track.Rig);   // a bypassed container bypasses the plug-ins inside it
        var now = ActiveOnRules.Enabled(track.Rig, chainOn, ActiveOutput);
        var key = string.Join(",", now);
        if (_sentBypass.TryGetValue(slot, out var sent) && sent == key) return;
        var before = sent?.Split(',');
        _sentBypass[slot] = key;
        for (var i = 0; i < now.Count; i++)
        {
            if (before is not null && i < before.Length && before[i] == now[i].ToString()) continue;
            var index = i; var enabled = now[i];
            Send(EngineCommand.SetPluginBypass, new SetPluginBypassMessage(slot, index, enabled).Write);
        }
    }

    /// <summary>
    /// The output changed (a device opened, a saved output fell back): match every live chain's Active on rules again and send only
    /// the bypass flags that differ. UI thread only; never called from the audio thread.
    /// </summary>
    private void ApplyActiveOnRules()
    {
        if (_ui is null) return;   // no UI context: nothing can have changed the output yet
        foreach (var (track, slot) in _slots.ToList()) SyncBypass(track, slot);
    }
}
