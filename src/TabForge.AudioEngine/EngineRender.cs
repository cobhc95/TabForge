using TabForge.Audio.Contracts;
using TabForge.AudioEngine.Mixing;

namespace TabForge.AudioEngine;

/// <summary>
/// Offline render on the engine process (EngineCommand.RenderOffline / RenderCancel, see Audio.Contracts/RenderProtocol.cs).
/// The engine main thread stops the audio device, puts the loaded chains into offline mode and hands them to an
/// <see cref="OfflineRenderer"/> on its own thread (the main thread keeps pumping window messages); when that ends,
/// whatever the outcome, the main thread restores the chains, reopens the device, replays the chain / clip changes that
/// arrived meanwhile, and only then reports RenderDone / RenderFailed.
/// </summary>
internal sealed partial class EngineSession
{
    /// <summary>Main thread writes; the reader thread (Panic) and the pitch worker read.</summary>
    public volatile bool RenderActive;
    /// <summary>Set by the reader thread (RenderCancel) or at shutdown; read by the render worker.</summary>
    public volatile bool RenderCancel;
    public Thread? RenderThread;
    private List<Action>? _deferred;

    /// <summary>Main thread: while a render runs, changes to chains, clips and the device wait until it is over. Returns true when <paramref name="action"/> was queued.</summary>
    private bool Defer(Action action)
    {
        if (!RenderActive) return false;
        (_deferred ??= new List<Action>()).Add(action);
        return true;
    }

    private static void FailRender(string message, bool cancelled = false, string plugin = "") =>
        Send(EngineEvent.RenderFailed, w => { w.WriteString(message); w.Write(cancelled); w.WriteString(plugin); });

    /// <summary>Main thread.</summary>
    public void BeginRender(RenderSpec spec)
    {
        AssertMain();
        if (RenderActive) { FailRender("A render is already running."); return; }
        if (Mix is null || Shared is null) { FailRender("The audio engine is not ready."); return; }
        RenderCancel = false;
        var renderer = new OfflineRenderer(spec, Mix, Shared, SampleRate, Math.Clamp(Config.BufferSize, 16, 8192), () => RenderCancel,
            p => Send(EngineEvent.RenderProgress, w => { w.Write(p.Fraction); w.Write(p.Seconds); w.Write(p.Speed); }));
        RenderActive = true;
        try
        {
            StopOutput();       // the render owns the chains now; the device stays closed until it ends
            renderer.Prepare();
        }
        catch (Exception ex)
        {
            EngineLog.Write($"render could not start: {ex}");
            FinishRender(renderer, null, ex);
            return;
        }
        RenderThread = new Thread(() =>
        {
            RenderResult? result = null; Exception? error = null;
            try { result = renderer.Run(); }
            catch (Exception ex) { error = ex; }
            EngineThreads.Post(() => FinishRender(renderer, result, error));
        }) { IsBackground = true, Name = "TabForge render" };
        RenderThread.Start();
    }

    /// <summary>Main thread, after the workers ended: puts everything back, then tells TabForge.</summary>
    private void FinishRender(OfflineRenderer renderer, RenderResult? result, Exception? error)
    {
        AssertMain();
        try { renderer.Restore(); }
        catch (Exception ex) { EngineLog.Write($"render restore failed: {ex}"); }
        RenderThread = null;
        RenderActive = false;
        try
        {
            if (Mix is not null && !EngineHost.Exiting) OpenOutput();
        }
        catch (Exception ex)
        {
            EngineLog.Write($"output could not be reopened after the render: {ex}");
            Send(EngineEvent.DeviceError, w => w.WriteString(ex.GetBaseException().Message));
        }
        var deferred = _deferred; _deferred = null;
        if (deferred is not null)
            foreach (var action in deferred)
            {
                try { action(); } catch (Exception ex) { EngineLog.Write($"deferred change after the render failed: {ex.GetBaseException().Message}"); }
            }
        if (result is not null) { Send(EngineEvent.RenderDone, w => result.Write(w)); return; }
        var cancelled = error is RenderException { Cancelled: true };
        FailRender(error is null ? "The render failed." : error is RenderException ? error.Message : $"The render failed: {error.GetBaseException().Message}",
            cancelled, error is RenderException re ? re.PluginPath : "");
    }
}
