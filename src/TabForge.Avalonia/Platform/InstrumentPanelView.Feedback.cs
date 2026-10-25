using System;
using Avalonia;
using Avalonia.Input;
using Avalonia.Threading;
using TabForge.Platform;

namespace TabForge.Avalonia.Platform;

// Owns: the click feedback of the keyboard keys and drum pads: the hover shade (theme HoverSurface),
//   the pressed shade (PressSurface), a short flash after a click, and the focus ring when the panel is reached by the keyboard.
// Does not own: the colours (the palette tokens), the picking (Pick) or the lit-note fills, which stay drawn on top of the shade.
// Tests: TabForge.Avalonia.Tests.InstrumentPanelFeedbackTests.
// Threads: UI thread only. Hover and press invalidate the panel only when the key under the pointer changes; no per-frame work.
public sealed partial class InstrumentPanelView
{
    private const string HoverKey = "HoverSurface", PressKey = "PressSurface";
    private static readonly TimeSpan FlashTime = TimeSpan.FromMilliseconds(140);
    // One reusable flash timer: a new press restarts it instead of allocating another.
    private DispatcherTimer? _flashTimer;

    private int? _hoverKey, _pressedKey, _flashKey;
    private bool _keyboardFocus;

    static InstrumentPanelView() => FocusableProperty.OverrideDefaultValue<InstrumentPanelView>(true);

    /// <summary>The key or pad under the pointer (keyboard and drum map), or null.</summary>
    internal int? HoveredKey => _hoverKey;

    /// <summary>Sets the hovered and pressed key without a pointer (tests).</summary>
    internal void SetFeedbackForTest(int? hover, int? pressed) { _hoverKey = hover; _pressedKey = pressed; InvalidateVisual(); }

    /// <summary>The key or pad held down, or null.</summary>
    internal int? PressedKey => _pressedKey;

    /// <summary>Keeps the hover shade in step with the pointer; redraws only when the hovered key changes.</summary>
    private void UpdateFeedback(Point at)
    {
        int? key = null;
        if (_kind != InstrumentPanelKind.Fretboard && Pick(new UiPoint(at.X, at.Y)) is { } pick) key = pick.Midi;
        if (key == _hoverKey) return;
        _hoverKey = key;
        InvalidateVisual();
    }

    private void BeginKeyPress(InstrumentNotePick pick)
    {
        if (_kind == InstrumentPanelKind.Fretboard) return;
        _pressedKey = _flashKey = pick.Midi;   // the next press replaces the previous flash key
        InvalidateVisual();
        _flashTimer ??= new DispatcherTimer(FlashTime, DispatcherPriority.Normal, (_, _) => { _flashTimer!.Stop(); _flashKey = null; InvalidateVisual(); });
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void EndKeyPress()
    {
        if (_pressedKey is null) return;
        _pressedKey = null;
        InvalidateVisual();
    }

    private void ClearKeyHover()
    {
        if (_hoverKey is null) return;
        _hoverKey = null;
        InvalidateVisual();
    }

    /// <summary>The shade a key or pad shows now: the pressed colour while held or flashing, the hover colour under the pointer, else null.</summary>
    private UiColor? KeyShade(int midi, Func<string, UiColor> ink) =>
        midi == _pressedKey || midi == _flashKey ? ink(PressKey) : midi == _hoverKey ? ink(HoverKey) : null;

    protected override void OnDetachedFromVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _flashTimer?.Stop();
        _flashTimer = null;   // a later press makes a fresh one
        _flashKey = null;
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        _keyboardFocus = e.NavigationMethod is NavigationMethod.Tab or NavigationMethod.Directional;
        InvalidateVisual();
    }

    protected override void OnLostFocus(global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        EndKeyPress();   // the key-up goes to whoever has focus now, so the press ends here
        if (!_keyboardFocus) return;
        _keyboardFocus = false;
        InvalidateVisual();
    }

    private void DrawFocusRing(IDrawContext d, Func<string, UiColor> ink)
    {
        DrawKeyFocusRing(d, ink);
        if (_keyboardFocus) d.Rect(null, new UiStroke(ink("InstrumentAccent"), 2), 1, 1, Math.Max(0, Bounds.Width - 2), Math.Max(0, Bounds.Height - 2));
    }
}
