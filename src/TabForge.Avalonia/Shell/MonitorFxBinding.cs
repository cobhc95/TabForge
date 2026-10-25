using System;

namespace TabForge.Avalonia.Shell;

/// <summary>
/// What the title bar's Monitor FX button reads and does: the monitor chain's state of the shown song, the actions of its click and menu, and the
/// change event that makes the button read again (raised by the owner on a mixer, settings or song change; never polled).
/// Owns: the delegates only. Does not own: the chain (FxChainWindows) or the button (CaptionButtons).
/// Tests: TabForge.Avalonia.Tests.MonitorFxTitleButtonTests.
/// </summary>
public sealed class MonitorFxBinding(Func<(bool On, int Plugins, bool Global)> read, Action open, Action<bool> setOn, Action<bool> setGlobal)
{
    public (bool On, int Plugins, bool Global) Read() => read();
    public void Open() => open();
    public void SetOn(bool on) => setOn(on);
    public void SetGlobal(bool global) => setGlobal(global);

    /// <summary>Raised when the chain's state may have changed; the button reads it again and repaints only if it differs.</summary>
    public event Action? Changed;
    public void RaiseChanged() => Changed?.Invoke();
}
