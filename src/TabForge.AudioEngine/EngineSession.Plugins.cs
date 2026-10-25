using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Editors;
using TabForge.AudioEngine.Mixing;
using TabForge.AudioEngine.Plugins;

namespace TabForge.AudioEngine;

// Owns: the engine session's plug-in editors, programs and state sends, split from EngineSession.cs.
// Does not own: the other parts of the engine session (EngineSession).
internal sealed partial class EngineSession
{
    // ---- Plug-ins: editors, programs, states ----

    private IPluginInstance? PluginAt(int slot, int index)
    {
        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || index < 0 || index >= request.Specs.Count) return null;
        return FindPlugin(chain, request.Specs[index]);
    }

    /// <summary>The loaded instance of one chain slot: by slot Id (two instances of one plug-in share a path), else by path.</summary>
    private static IPluginInstance? FindPlugin(TrackChain chain, PluginSpec spec)
        => spec.Id.Length > 0 && chain.ById.TryGetValue(spec.Id, out var byId) ? byId : chain.Plugins.FirstOrDefault(p => p.Path == spec.Path);

    public void SendPrograms(int slot, int index)
    {
        AssertMain();
        var plugin = PluginAt(slot, index);
        // The program list runs plug-in code (effGetProgramNameIndexed × N): attribute a hang like every main-thread call.
        // (Vst2Plugin.Programs holds its own fallback breadcrumb with slot -1; this outer one carries the real slot/index.)
        var entered = EnterCall(slot, index, plugin, plugin?.Path ?? "", PluginCallKind.Other);
        IReadOnlyList<string> names;
        int current;
        try
        {
            names = plugin is Vst2Plugin v ? v.Programs : Array.Empty<string>();
            current = plugin is Vst2Plugin c ? c.CurrentProgram : -1;
        }
        finally { LeaveCall(entered); }
        Send(EngineEvent.Programs, w => { w.Write(slot); w.Write(index); w.Write(current); w.Write(names.Count); foreach (var n in names) w.WriteString(n); });
    }

    public void OpenEditor(int slot, int index, IntPtr owner, bool dark, bool docked = false, bool onTop = false)
    {
        AssertMain();
        Log.Write($"editor requested: slot {slot} plug-in {index}");
        if (owner != IntPtr.Zero && !EditorWindows.IsWindow(owner)) { Log.Write("editor: the parent window no longer exists"); owner = IntPtr.Zero; }
        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || index < 0 || index >= request.Specs.Count)
        {
            Log.Write($"editor: no chain for slot {slot} / plug-in {index}");
            return;
        }
        var path = request.Specs[index].Path;
        var plugin = FindPlugin(chain, request.Specs[index]);
        if (plugin is null) { Log.Write($"editor: {path} is not loaded in slot {slot}"); return; }
        void Size(int w, int h) => Send(EngineEvent.EditorSize, x => { x.Write(slot); x.Write(index); x.Write(w); x.Write(h); });
        if (plugin is Isolation.RemotePlugin remote)   // its window lives in its own process
        {
            remote.EditorSized = Size;
            if (remote.OpenEditor(owner, docked) is { } opened) Size(opened.Width, opened.Height);
            return;
        }
        var spec = request.Specs[index];
        var name = spec.Name.Length > 0 ? spec.Name : Path.GetFileNameWithoutExtension(path);
        Editors.UserClosed ??= () => Send(EngineEvent.EditorClosed, _ => { });
        var entered = EnterCall(slot, index, plugin, path, PluginCallKind.Editor);
        bool shown;
        try { shown = Editors.Show(plugin, $"{name} — {request.Track}", owner, dark, docked, onTop, Size); }
        finally { LeaveCall(entered); }
        if (!shown) Log.Write($"editor: {path} has no editor or its window could not be created");
    }

    /// <summary>
    /// Answers a state request with one <see cref="EngineEvent.PluginState"/> frame per plug-in and an explicit status each, so TabForge
    /// knows exactly which states it has (a failed getter is reported, never replaced by the old stored state). Each GetState call carries
    /// the plug-in breadcrumb, so the main-thread watchdog can end a hung getter and blame that plug-in.
    /// </summary>
    public void SendStates(int slot, int requestId)
    {
        AssertMain();
        void Reply(int index, int count, PluginStateStatus status, string text) =>
            Send(EngineEvent.PluginState, w => { w.Write(slot); w.Write(requestId); w.Write(index); w.Write(count); w.Write((byte)status); w.WriteString(text); });

        if (!Loaded.TryGetValue(slot, out var chain) || !Requested.TryGetValue(slot, out var request) || request.Specs.Count == 0)
        {
            Reply(-1, 0, PluginStateStatus.NoState, "");
            return;
        }
        var count = request.Specs.Count;
        for (var index = 0; index < count; index++)
        {
            var spec = request.Specs[index];
            // By id: two copies of one plug-in each report their own state; bypassed ones too.
            var plugin = chain.ById.GetValueOrDefault(spec.Id)
                ?? (Parked.TryGetValue(slot, out var parked) ? parked.GetValueOrDefault(spec.Id) : null)
                ?? (spec.Id.Length == 0 ? chain.Plugins.FirstOrDefault(p => p.Path == spec.Path) : null);
            var status = PluginStateStatus.NoState;
            var text = "";
            if (plugin is not null)
            {
                var entered = EnterCall(slot, index, plugin, spec.Path, PluginCallKind.GetState);
                try
                {
                    var state = plugin.GetState();
                    if (state is null) status = PluginStateStatus.NoState;
                    else if (state.Length > PluginStateLimits.MaxRawBytes) status = PluginStateStatus.TooLarge;
                    else { status = PluginStateStatus.Captured; text = Convert.ToBase64String(state); }
                }
                catch (InvalidDataException ex) { Log.Write($"get state: {ex.Message}"); status = PluginStateStatus.TooLarge; }
                catch (TimeoutException ex) { Log.Write($"get state timed out: {ex.Message}"); status = PluginStateStatus.TimedOut; }
                catch (Exception ex) { Log.Write($"get state failed: {ex.Message}"); status = PluginStateStatus.Failed; }
                finally { LeaveCall(entered); }
            }
            Reply(index, count, status, text);
        }
    }
}
