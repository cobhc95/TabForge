using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Styling;
using TabForge.Avalonia.Shell.Win32;
using TabForge.Avalonia.Views.Fx;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: the plug-in area and the window's life: what shows for the focused row (the container pane, or the plug-in bar and its editor notice or docked editor),
//   the notice buttons (Allow, Allow again, Show it here, Retry, Open in separate window), the BYPASSED stamp, the editor following the window, the keys (the FX chain keys, Escape), the
//   one-second CPU tick, and the clean-up and state saving when the window closes.
// Does not own: the editor state machine (FxEditorController), the area layout (FxEditorArea), the Windows calls (PluginEditorWin32), the host.
// Tests: TabForge.Avalonia.Tests.FxChainKeysTests, TabForge.Avalonia.Tests.FxBypassOverlayTests.
public sealed partial class FxChainWindow
{
    private IntPtr _popup;
    private PluginSlot? _shownSlot;

    /// <summary>The window handle of the main window (the owner of floating plug-in windows); zero without a native window.</summary>
    private IntPtr OwnerHandle => Platform.PlatformAdapters.NativeWindows.Of(Owner as Window) is var owner && owner != IntPtr.Zero ? owner : SelfHandle;

    private IntPtr SelfHandle => Platform.PlatformAdapters.NativeWindows.Of(this);

    /// <summary>The engine's docked editor window of this window, or zero.</summary>
    private IntPtr PluginEditorPopup => _popup = PluginEditorWin32.FindEditorPopup(SelfHandle, _popup);

    /// <summary>Shows the focused row: the container pane for a container, else the plug-in's bar and its editor or notice.</summary>
    private void ShowSelected()
    {
        if (_closed) return;
        var container = _list.Focused as ChainContainer;
        _bar.IsVisible = !_containers.Show(container);
        if (container is not null)
        {
            _editors.CloseDocked();
            _containerPresets.Fill();
            _area.ShowNotice("", null);
            ShowParameters(null, false);
            UpdateOverlay();
            return;
        }
        var slot = Selected;
        if (!ReferenceEquals(slot, _shownSlot)) { _editors.ResetPrograms(); _shownSlot = slot; _fallbackOff = false; if (BuiltinEditors.IsBuiltin(slot)) _parametersDown = false; }
        ShowBar();
        // A built-in effect's editor is embedded in the editor area (ShowParameters); the generic controls replace it via "Show generic controls" or when it has no editor.
        var builtin = BuiltinEditors.IsBuiltin(slot);
        var ownWindow = BuiltinEditors.HasWindow(slot);
        var viaButton = builtin ? (_parametersDown || !ownWindow) && FxGenericControls.Supports(slot) : _parametersDown && FxGenericControls.CanForce(FxGenericControls.Supports(slot), _editors.ModeOf(slot));   // the Parameters button / "Show generic controls" replaces the editor
        if (viaButton) { _editors.CloseDocked(); _area.ShowNotice("", null); }
        else if (ownWindow) { _editors.CloseDocked(); _area.ShowNotice("", null); }
        else _editors.Show(slot);
        ShowParameters(slot, viaButton);
        UpdateOverlay();
    }

    /// <summary>The BYPASSED stamp covers the editor area while the focused plug-in does not play or the chain is off. The editor is another process window
    /// that would cover the stamp, so the area hides its native window while the stamp shows (the engine then hides the popup, and shows it again after).</summary>
    private void UpdateOverlay()
    {
        var slot = Selected;
        var show = slot is not null && _editors.ModeOf(slot) == FxEditorMode.Docked && _area.EditorVisible
            && FxChainPresenter.ShowsBypassOverlay(slot.Plays, _track.SoundSource == SoundSources.Plugins);
        _area.ShowOverlay(show, ActualThemeVariant == ThemeVariant.Light);
    }

    private void OnAreaAction()
    {
        if (Selected is not { } slot) return;
        switch (_area.ActionLabel)
        {
            case FxEditorPlan.AllowAgainButton:
                // Quarantine only: trust and approval records stay as they are (an untrusted file is still blocked by the trust check).
                if (PluginQuarantine.AllowAgain(_host.PluginSettings.Quarantined, slot.Path) > 0)
                {
                    _host.SaveSettings();
                    _host.SyncEngine(_track);   // the next engine sync loads it again (its chain key no longer says Skip)
                }
                break;
            case FxEditorPlan.AllowButton:
                if (!_host.Trust.IsTrusted(slot.Path, _host.PluginSettings, _host.PluginFiles))
                {
                    _host.Trust.Approve(_host.PluginSettings, slot.Path, _host.PluginFiles);
                    _host.SaveSettings();
                    _host.SyncEngine(_track);
                }
                break;
            case FxEditorPlan.RetryButton:
                _fallbackOff = false;
                _editors.Retry(slot);
                break;
            default:
                _editors.Redock(slot);
                break;
        }
        ShowSelected();
    }

    private void OnAreaSecondAction()
    {
        if (Selected is { } slot && !BuiltinEditors.TryOpen(this, _host, _track, slot)) _editors.Float(slot);
        ShowSelected();
    }

    /// <summary>Keeps the window within its screen, so the title buttons (close) are never pushed off the edge; the strip wraps to whatever width is left.</summary>
    private void FitToScreen() => WindowMetrics.FitToScreen(this);

    private void AttachWindowEvents()
    {
        AddHandler(KeyDownEvent, OnWindowKey, global::Avalonia.Interactivity.RoutingStrategies.Tunnel);
        SizeChanged += (_, _) => { _splitter?.Reapply(); FollowEditor(); };
        PositionChanged += (_, _) => FollowEditor();
        Opened += (_, _) => FitToScreen();
        Activated += (_, _) => FollowEditor();
        ActualThemeVariantChanged += (_, _) => UpdateOverlay();
        _cpuTimer.Tick += (_, _) => { FollowEditor(); UpdateOverlay(); };
        _cpuTimer.Start();
        Closed += (_, _) => OnClosed();
    }

    // Moving the window moves the docked editor (an engine-owned popup) in the same event, so it neither lags nor flashes while dragging.
    private void FollowEditor()
    {
        if (_editors.Docked is null) return;
        PluginEditorWin32.FollowArea(SelfHandle, _area.Surface.Area, PluginEditorPopup);
    }

    // The FX chain keys first (Ctrl+S, Ctrl+O, Ctrl+G ... as bound), then Escape: cancels a drag or band, else closes unless a text box or a drop-down has focus.
    private void OnWindowKey(object? sender, KeyEventArgs e)
    {
        var focused = FocusManager?.GetFocusedElement();
        if (_keys.Handle(e.Key, e.KeyModifiers, focused, ListFocused)) { e.Handled = true; return; }
        if (e.Key != Key.Escape || e.KeyModifiers != KeyModifiers.None) return;
        if (_optionsButton.Flyout is { IsOpen: true } open) { open.Hide(); e.Handled = true; return; }   // Escape closes the open Options popup, not the window
        if (_list.HandleEscape()) { e.Handled = true; return; }
        if (focused is TextBox or ComboBox) return;
        e.Handled = true;
        Close();
    }

    /// <summary>Closes the window because an undo, a redo or a reload replaced its track: the chain templates are not saved from the stale track.</summary>
    public void CloseReplaced()
    {
        _replaced = true;
        Close();
    }

    private async void OnClosed()
    {
        _closed = true;
        _cpuTimer.Stop();
        _parameters.Dispose();
        if (_pitchMatcher is not null) _pitchMatcher.Changed -= OnPitchChanged;
        _host.SettingsChanged -= OnSettingsChanged;
        // The native plug-in editors go down after the window is gone, not on the click that closed it (at shutdown there is no later).
        if (_ui.IsShuttingDown) _editors.Dispose(); else _ui.Post(_editors.Dispose, TabForge.Platform.UiPriority.Background);
        // Keep the templates in step with the chain as it was left (plug-in states included): the app-wide monitor chain and the startup track.
        // The dispatcher's shutdown starts after the windows closed, so the states are still collected at exit; the hold keeps the store open for the save.
        if (_host.Engine is not { } engine || _replaced) return;   // a window replaced by an undo must not save its stale track as the template
        using var hold = _host.HoldSettingsOpen();
        try
        {
            if (MixerBuses.IsMonitor(_track) && _host.MonitorUseGlobal)
            {
                await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
                _host.SaveSettingsQuietly();
            }
            if (!_track.IsBus && StartupTracks.Has(_host.PluginSettings, _track))
            {
                await engine.CollectStatesAsync(new[] { _track }, StateCollectMs);
                StartupTracks.Save(_host.PluginSettings, _track);
                _host.SaveSettingsQuietly();
            }
        }
        catch (Exception ex)
        {
            Trace.Error(Trace.Engine, "FX chain: save after edit: " + ex.Message);
        }
    }
}
