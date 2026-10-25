using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Models;
using TabForge.Plugins;
using TabForge.Presenters;
using TabForge.Platform;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: which plug-in editor shows in the FX chain window: the docked one (the engine parents it to the area), the floating windows the user opened,
//   the plug-in just added whose load is still running, the program names the engine reports, and the notice for every state (FxEditorPlan).
// Does not own: the chain (FxChainWindow), the area it fills (FxEditorArea), the engine (IFxChainEngine), trust and quarantine rules (PluginTrust, PluginQuarantine).
// Tests: no dedicated test class.
// Threads: UI thread only; the engine events arrive on it.
public sealed class FxEditorController : IDisposable
{
    /// <summary>How long a docked editor may take to show its window before the area says it did not open; the first open of a plug-in (its load may still run) gets <see cref="FirstOpenCheckMs"/>.</summary>
    public const int OpenCheckMs = 1500, FirstOpenCheckMs = 5000;

    private readonly IFxChainHost _host;
    private readonly TrackModel _track;
    private readonly FxEditorArea _area;
    private readonly IUiThread _ui;
    private readonly Func<IntPtr> _ownerHandle;
    private readonly Func<bool> _dark;
    private readonly Func<PluginSlot?> _focused;
    private readonly Action _reshow;
    private readonly Action _editorClosed;
    private readonly Func<IntPtr>? _popup;
    private readonly IUiTimer? _openCheck;
    private readonly FloatingEditors.Set _floating;   // floating plug-in windows outlive the FX window that opened them: the registry holds them per track and song
    private readonly HashSet<PluginSlot> _opened = new(ReferenceEqualityComparer.Instance);   // docked editors that showed once: their next open is quick
    private PluginSlot? _docked;
    private PluginSlot? _loading;
    private PluginSlot? _failed;

    /// <param name="focused">The focused plug-in row, or null.</param>
    /// <param name="reshow">Asks the window to show the focused row again (the area got its native window, or the chain was rebuilt).</param>
    /// <param name="editorClosed">A floating plug-in window was closed by the user (the FX window takes the focus back).</param>
    /// <param name="popup">The engine's docked editor window of the FX window (zero when there is none); without it a docked editor that never appears is not noticed.</param>
    public FxEditorController(IFxChainHost host, TrackModel track, FxEditorArea area, IUiThread ui, Func<IntPtr> ownerHandle, Func<bool> dark,
        Func<PluginSlot?> focused, Action reshow, Action editorClosed, Func<IntPtr>? popup = null)
    {
        (_host, _track, _area, _ui, _ownerHandle, _dark, _focused, _reshow, _editorClosed, _popup) = (host, track, area, ui, ownerHandle, dark, focused, reshow, editorClosed, popup);
        _floating = FloatingEditors.Of(track);
        if (popup is not null)
        {
            _openCheck = ui.CreateTimer(TimeSpan.FromMilliseconds(OpenCheckMs));
            _openCheck.Tick += (_, _) => CheckOpened();
        }
        if (host.Engine is { } engine)
        {
            engine.EditorSized += OnEditorSized;
            engine.ProgramsReceived += OnPrograms;
            engine.ChainLoaded += OnChainLoaded;
            engine.EditorClosed += OnEditorClosed;
        }
        area.Surface.AreaChanged += OnAreaChanged;
    }

    /// <summary>The program names of the focused plug-in, as the engine last reported them.</summary>
    public IReadOnlyList<string> Programs { get; private set; } = Array.Empty<string>();
    /// <summary>The engine's current program of the focused plug-in (-1: unknown).</summary>
    public int CurrentProgram { get; private set; } = -1;
    /// <summary>The program names changed (the preset box refills).</summary>
    public event Action? ProgramsChanged;
    /// <summary>Forgets the program names (another plug-in is focused).</summary>
    public void ResetPrograms() { Programs = Array.Empty<string>(); CurrentProgram = -1; }
    public PluginSlot? Docked => _docked;
    public bool IsFloating(PluginSlot slot) => _floating.Contains(slot);

    /// <summary>Marks a plug-in whose engine load is running; its editor opens when the engine reports the chain loaded.</summary>
    public void MarkLoading(PluginSlot slot) => _loading = _host.Engine?.IsRunning == true ? slot : null;

    /// <summary>The mode the focused plug-in is in now.</summary>
    public FxEditorMode ModeOf(PluginSlot? slot)
    {
        var engine = _host.Engine;
        var settings = _host.PluginSettings;
        return FxEditorPlan.ModeFor(slot is not null, slot is null || _host.Trust.IsTrusted(slot.Path, settings, _host.PluginFiles),
            slot is not null && PluginQuarantine.Contains(settings.Quarantined, slot.Path),
            engine is { IsRunning: true } && engine.SlotOf(_track) >= 0, slot is not null && ReferenceEquals(slot, _loading),
            settings.DockPluginWindows, slot is not null && _floating.Contains(slot));
    }

    /// <summary>Shows the area for the focused plug-in (null: none): its notice and button, or the docked editor.</summary>
    public FxEditorMode Show(PluginSlot? slot)
    {
        if (slot is not null && !ReferenceEquals(slot, _docked)) CloseDocked();
        var mode = ModeOf(slot, out var settings);
        var button = FxEditorPlan.ButtonFor(mode, settings.DockPluginWindows);
        if (mode is FxEditorMode.Quarantined or FxEditorMode.Unloaded) _docked = null;   // the engine has no editor for it: it opens again when the chain is back
        if (mode != FxEditorMode.Docked)
        {
            var notice = FxEditorPlan.NoticeFor(mode, slot?.Path ?? "", _track.SoundSource == SoundSources.Plugins, slot?.Plays ?? false, slot?.Enabled ?? false);
            _area.ShowNotice(notice, button);
            if (mode == FxEditorMode.Floating && !settings.DockPluginWindows && slot is not null && !_floating.Contains(slot)) Float(slot);
            return mode;
        }
        if (ReferenceEquals(_failed, slot)) { ShowFailed(); return mode; }
        _area.ShowEditor();
        var engine = _host.Engine!;
        engine.RequestPrograms(_track, slot!);
        if (_area.Surface.Area != IntPtr.Zero && !ReferenceEquals(_docked, slot))
        {
            _docked = slot;
            OpenDocked(slot!);
        }
        return mode;
    }

    /// <summary>Asks the engine to dock the editor of <paramref name="slot"/>; when it refuses, or its window does not appear in time, the area says so.</summary>
    private void OpenDocked(PluginSlot slot)
    {
        _openCheck?.Stop();
        if (_host.Engine?.OpenEditor(_track, slot, _area.Surface.Area, _dark(), docked: true, onTop: false) != true) { Fail(slot); return; }
        if (_openCheck is null) return;
        _openCheck.Interval = TimeSpan.FromMilliseconds(_opened.Contains(slot) ? OpenCheckMs : FirstOpenCheckMs);
        _openCheck.Start();
    }

    private void CheckOpened()
    {
        _openCheck?.Stop();
        if (_docked is not { } docked) return;
        if (_popup!() == IntPtr.Zero) Fail(docked);
        else _opened.Add(docked);
    }

    private void Fail(PluginSlot slot)
    {
        _failed = slot;
        if (ReferenceEquals(_focused(), slot)) ShowFailed();
    }

    private void ShowFailed() => _area.ShowNotice(FxEditorPlan.EditorFailedNotice, FxEditorPlan.RetryButton, FxEditorPlan.SeparateWindowButton);

    /// <summary>Retry: the docked editor is closed and opened again the next time the row shows.</summary>
    public void Retry(PluginSlot slot)
    {
        if (ReferenceEquals(_docked, slot)) CloseDocked();
        else _failed = null;
    }

    private FxEditorMode ModeOf(PluginSlot? slot, out PluginSettings settings)
    {
        settings = _host.PluginSettings;
        return ModeOf(slot);
    }

    /// <summary>Gives the plug-in its own window outside the FX window (a double-click, or docking turned off).</summary>
    public void Float(PluginSlot slot)
    {
        if (ReferenceEquals(_docked, slot)) CloseDocked();
        _floating.Add(slot, (_host as SongFxChainHost)?.Document, _host.Engine, _host.PluginSettings.PluginWindowsOnTop);
        var opened = _host.Engine?.OpenEditor(_track, slot, _ownerHandle(), _dark(), docked: false, onTop: _host.PluginSettings.PluginWindowsOnTop) == true;
        if (!opened) _area.ShowNotice(FxEditorPlan.FloatNeedsChainNotice, null);
        else if (ReferenceEquals(_focused(), slot)) _area.ShowNotice(FxEditorPlan.FloatingNotice, FxEditorPlan.ButtonFor(FxEditorMode.Floating, _host.PluginSettings.DockPluginWindows));
    }

    /// <summary>Show it here: the plug-in's window goes back into the area.</summary>
    public void Redock(PluginSlot slot)
    {
        if (_floating.Remove(slot)) _host.Engine?.CloseEditor(_track, slot);
    }

    /// <summary>Forgets every floating window (the option to show plug-in windows here was turned on).</summary>
    public void ForgetFloating() => _floating.Clear();

    /// <summary>Closes the editors of plug-ins that leave the chain.</summary>
    public void Leaving(IEnumerable<PluginSlot> slots)
    {
        foreach (var slot in slots.ToList())
        {
            if (ReferenceEquals(_docked, slot)) CloseDocked();
            if (_floating.Remove(slot)) _host.Engine?.CloseEditor(_track, slot);
            if (ReferenceEquals(_loading, slot)) _loading = null;
            _opened.Remove(slot);
        }
    }

    public void CloseDocked()
    {
        _openCheck?.Stop();
        _failed = null;
        if (_docked is { } slot) _host.Engine?.CloseEditor(_track, slot);
        _docked = null;
    }

    private void OnAreaChanged()
    {
        if (_area.Surface.Area != IntPtr.Zero) _ui.Post(_reshow, UiPriority.Loaded);
    }

    private void OnChainLoaded()
    {
        if (_openCheck is { IsEnabled: true } check) { check.Stop(); check.Start(); }   // an open waiting for the load is timed from the load
        if (_loading is null)
        {
            // A rebuilt chain may have new plug-in instances (their old editors closed): open the docked one again. An instance whose editor survived is just re-placed.
            if (_docked is { } docked && _area.Surface.Area != IntPtr.Zero && _area.EditorVisible && _track.Rig.Plugins.Contains(docked))
                _ui.Post(() => { if (ReferenceEquals(_docked, docked)) OpenDocked(docked); }, UiPriority.ApplicationIdle);
            return;
        }
        _loading = null;
        _ui.Post(_reshow, UiPriority.ApplicationIdle);
    }

    private void OnEditorSized(int engineSlot, int index, int width, int height)
    {
        if (_docked is not { } docked || engineSlot != _host.Engine?.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(docked)) return;
        _openCheck?.Stop();   // the engine opened it (a slow plug-in answers after the check would have given up)
        _opened.Add(docked);
        _area.SetEditorSize(width, height);
        if (ReferenceEquals(_failed, docked)) { _failed = null; _ui.Post(_reshow, UiPriority.ApplicationIdle); }
    }

    private void OnPrograms(int engineSlot, int index, int current, IReadOnlyList<string> names)
    {
        if (_focused() is not { } slot || engineSlot != _host.Engine?.SlotOf(_track) || index != _track.Rig.Plugins.IndexOf(slot)) return;
        Programs = names.ToList();
        CurrentProgram = current;
        ProgramsChanged?.Invoke();
    }

    // The engine's event names no plug-in: the only floating one, else the focused one, is the window the user closed (it goes back to docked).
    private void OnEditorClosed()
    {
        if (_floating.Count == 1) _floating.Clear();
        else if (_focused() is { } slot) _floating.Remove(slot);
        _ui.Post(_editorClosed);
    }

    public void Dispose()
    {
        if (_host.Engine is { } engine)
        {
            engine.EditorClosed -= OnEditorClosed;
            engine.EditorSized -= OnEditorSized;
            engine.ProgramsReceived -= OnPrograms;
            engine.ChainLoaded -= OnChainLoaded;
        }
        // The floating plug-in windows stay open (the engine owns them; they close with their plug-in, track or the app).
        _area.Surface.AreaChanged -= OnAreaChanged;
        CloseDocked();
    }
}
