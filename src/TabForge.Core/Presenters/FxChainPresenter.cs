namespace TabForge.Presenters;

/// <summary>What the Remove button takes: the selected containers (with what is inside), the selected plug-ins, or nothing.</summary>
public enum RemoveTarget { Nothing, Containers, Plugins }

/// <summary>
/// The FX chain window's labels, notices and rules that need no toolkit types: the window title, the chain switch's label for a bus
/// or the monitor, the bar's bypass switch, the notice for the selected plug-in, the CPU status text, the output dB rounding, the Remove rules and when the
/// GM sound plays again. Every value comes in as a plain flag, count or number.
/// Owns: those texts and rules.
/// Does not own: the chain and its plug-ins (PluginSlot, ChainTree), the trust and quarantine checks (PluginTrust, PluginQuarantine),
///     the engine and its CPU reading, the docked editor and the removal itself (FxChainWindow).
/// Tests: FxChainWindowTests.
/// </summary>
public static class FxChainPresenter
{
    public const string NoPluginsNotice = "No plug-ins yet. Use Add… (or the FX menu) to choose a VST instrument or effect.";
    public const string ChainOffNotice = "The chain is off. Tick “Play this track through the chain” (or the FX power switch) to use the plug-in.";
    public const string StartingNotice = "Starting the plug-in…";
    public const string ContainerBypassedNotice = "The container this plug-in sits in is bypassed.";
    public const string PluginBypassedNotice = "This plug-in is bypassed.";
    public const string EditorUnavailableNotice = "The plug-in editor cannot be docked in this window on this system. The chain can still be edited.";

    public const string BypassContent = "On";
    public const string BypassToolTip = "Untick to bypass this plug-in";

    public const string BusUseChainContent = "Bus on";
    public const string BusUseChainToolTip = "Untick to bypass this chain: the summed audio passes straight on to the master";
    public const string MonitorTitle = "FX: Monitor (live only, never rendered)";
    public const string MonitorUseChainContent = "Monitor on";
    public const string MonitorUseChainToolTip = "Untick to bypass the monitoring effects. They play after the master, live only; never included in renders or exports.";

    public const string CpuToolTip = "CPU: the audio engine's share of the whole processor, as the system's task list shows it. Load (audio load): how much of each audio buffer's time all chains use on one core; dropouts start near 100%.";

    /// <summary>The FX window's title for a track.</summary>
    public static string WindowTitle(string trackName) => $"FX: {trackName}";

    /// <summary>The notice when the chain has no plug-in selected to show.</summary>
    public static string BlockedNotice(string path) =>
        $"Blocked: not approved, or the file changed since you approved it. It is not loaded (use Allow only if you trust it):\n{path}";

    /// <summary>The notice for a plug-in that was switched off after a crash; the file name is shown, not the full path.</summary>
    public static string QuarantinedNotice(string path) =>
        $"Switched off after a crash. It is not loaded until you allow it again:\n{System.IO.Path.GetFileName(path.TrimEnd('\\', '/'))}";

    /// <summary>The notice for a trusted plug-in the engine has not loaded: the chain is off, it is starting, its container is bypassed, or it is bypassed.</summary>
    public static string UnloadedNotice(bool chainOn, bool plays, bool enabled) =>
        !chainOn ? ChainOffNotice : plays ? StartingNotice : enabled ? ContainerBypassedNotice : PluginBypassedNotice;

    /// <summary>The status text beside the Add and Remove buttons: the engine's CPU share and audio load, or nothing when the engine is stopped.</summary>
    public static string CpuStatusText(bool engineRunning, double cpuLoad, int processorCount) =>
        engineRunning ? $"CPU {cpuLoad / processorCount:P1} · load {cpuLoad:P0}" : "";

    /// <summary>The bypass overlay covers the docked editor when the plug-in does not play or the chain is switched off.</summary>
    public static bool ShowsBypassOverlay(bool plays, bool chainOn) => !plays || !chainOn;

    /// <summary>Remove takes the selected containers when any is selected, else the selected plug-ins, and nothing when none is.</summary>
    public static RemoveTarget ChooseRemove(bool containerSelected, int selectedPluginCount) =>
        containerSelected ? RemoveTarget.Containers : selectedPluginCount == 0 ? RemoveTarget.Nothing : RemoveTarget.Plugins;

    /// <summary>The output gain the output slider sets: its value rounded to one decimal dB.</summary>
    public static double OutputDbFromSlider(double sliderValue) => Math.Round(sliderValue, 1);

    /// <summary>The chain's last instrument left it: the GM sound plays again.</summary>
    public static bool RestoresGmSound(bool hadInstrument, bool hasInstrumentNow) => hadInstrument && !hasInstrumentNow;
}
