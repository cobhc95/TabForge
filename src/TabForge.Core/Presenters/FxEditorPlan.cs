namespace TabForge.Presenters;

/// <summary>What the plug-in area of the FX chain window shows for the focused row.</summary>
public enum FxEditorMode
{
    /// <summary>No plug-in is focused (an empty chain, or a container is).</summary>
    None,
    /// <summary>The plug-in is not approved, or its file changed: it is not loaded.</summary>
    Blocked,
    /// <summary>The plug-in crashed and is switched off until it is allowed again.</summary>
    Quarantined,
    /// <summary>The engine has no chain for the track (not running, or the chain is off).</summary>
    Unloaded,
    /// <summary>The plug-in was just added and the engine is still creating it.</summary>
    Starting,
    /// <summary>The plug-in has its own window outside the FX window.</summary>
    Floating,
    /// <summary>The plug-in's editor is shown inside the plug-in area.</summary>
    Docked,
}

/// <summary>
/// The decision the FX chain window makes each time the focused row changes: which of the plug-in area's states applies, the sentence it shows
/// and the button beside it. The states come in the order the checks run: a plug-in that is not allowed never reaches the engine.
/// Owns: that order and the texts that are not in <see cref="FxChainPresenter"/>. Does not own: trust or quarantine checks (PluginTrust, PluginQuarantine), the editor itself.
/// Tests: no dedicated test class.
/// </summary>
public static class FxEditorPlan
{
    public const string FloatingNotice = "This plug-in's window is floating.";
    public const string FloatNeedsChainNotice = "The plug-in window can be opened when the chain is on.";
    public const string EditorFailedNotice = "This plug-in's window didn't open.";
    public const string RetryButton = "Retry", SeparateWindowButton = "Open in separate window";
    public const string AllowButton = "Allow", AllowAgainButton = "Allow again", ShowHereButton = "Show it here";

    /// <param name="hasPlugin">A plug-in row is focused.</param>
    /// <param name="trusted">PluginTrust allows loading it.</param>
    /// <param name="quarantined">It is on the crash list.</param>
    /// <param name="engineHasChain">The engine runs and has a chain for this track.</param>
    /// <param name="starting">It was just added and the engine has not reported the chain loaded.</param>
    /// <param name="dockSetting">The option "Show plug-in windows in this window".</param>
    /// <param name="floatingOnRequest">The user floated this plug-in's window.</param>
    public static FxEditorMode ModeFor(bool hasPlugin, bool trusted, bool quarantined, bool engineHasChain, bool starting, bool dockSetting, bool floatingOnRequest)
    {
        if (!hasPlugin) return FxEditorMode.None;
        if (!trusted) return FxEditorMode.Blocked;
        if (quarantined) return FxEditorMode.Quarantined;
        if (!engineHasChain) return FxEditorMode.Unloaded;
        if (starting) return FxEditorMode.Starting;
        return !dockSetting || floatingOnRequest ? FxEditorMode.Floating : FxEditorMode.Docked;
    }

    /// <summary>The label of the button beside the notice for a mode, or null when there is none. Floating gets "Show it here" only when docking is on.</summary>
    public static string? ButtonFor(FxEditorMode mode, bool dockSetting) => mode switch
    {
        FxEditorMode.Blocked => AllowButton,
        FxEditorMode.Quarantined => AllowAgainButton,
        FxEditorMode.Floating when dockSetting => ShowHereButton,
        _ => null,
    };

    /// <summary>The notice for a mode and the focused plug-in; empty for the docked editor.</summary>
    public static string NoticeFor(FxEditorMode mode, string path, bool chainOn, bool plays, bool enabled) => mode switch
    {
        FxEditorMode.None => FxChainPresenter.NoPluginsNotice,
        FxEditorMode.Blocked => FxChainPresenter.BlockedNotice(path),
        FxEditorMode.Quarantined => FxChainPresenter.QuarantinedNotice(path),
        FxEditorMode.Unloaded => FxChainPresenter.UnloadedNotice(chainOn, plays, enabled),
        FxEditorMode.Starting => FxChainPresenter.StartingNotice,
        FxEditorMode.Floating => FloatingNotice,
        _ => "",
    };
}
