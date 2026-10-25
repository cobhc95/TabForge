using System;
using TabForge.Avalonia.Platform;
using TabForge.Plugins;

namespace TabForge.Avalonia.Shell;

// Owns: the services every window of the app shares, made once when the first window is composed (TabWindowRegistry holds them): the plug-in trust
//   state and the one meter tick. Windows are handed these; nothing reaches them through a static.
// Does not own: what the services do (PluginTrust, SharedMeterTick) or the windows' lifetimes.
// Tests: no dedicated test class.
internal sealed class AppServices : IDisposable
{
    /// <param name="trust">The ONE trust state of the app, shared with the engine's load gate (so a file it finds changed is untrusted in every window too). Required.</param>
    public AppServices(PluginTrust trust) => Trust = trust;

    public PluginTrust Trust { get; }

    public SharedMeterTick MeterTick { get; } = new();

    public void Dispose() => MeterTick.Dispose();
}
