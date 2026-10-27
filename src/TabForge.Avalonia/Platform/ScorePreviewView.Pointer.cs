using Avalonia.Input;
using Avalonia.Interactivity;

namespace TabForge.Avalonia.Platform;

// Owns: the toolkit pointer, focus and key overrides of the score preview, which hand each event to the input controller (_input) and raise Focused.
// Does not own: what a press, move or key does (the score input controller and Core ScorePreviewEditor).
// Tests: TabForge.Avalonia.Tests.ScorePreviewEditorTests.
public sealed partial class ScorePreviewView
{
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var props = e.GetCurrentPoint(this).Properties;
        if (props.IsRightButtonPressed && !props.IsLeftButtonPressed) _input.PressRight(e);   // a right drag selects without a loop; a plain right click still opens the menu
        if (!props.IsLeftButtonPressed) return;
        Focus();
        Focused?.Invoke();
        _input.Press(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        // The release of a right drag opens no context menu: the base would raise ContextRequested for it.
        var swallowsMenu = e.InitialPressMouseButton == MouseButton.Right && _input.RightHoldSwallowsMenu;
        if (!swallowsMenu) base.OnPointerReleased(e);
        _input.Release(e);
        if (swallowsMenu) e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        _input.Move(e);
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        base.OnPointerCaptureLost(e);
        _input.CaptureLost();
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        _input.Exited();
    }

    protected override void OnGotFocus(GotFocusEventArgs e)
    {
        base.OnGotFocus(e);
        Focused?.Invoke();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        _input.KeyDown(e);
    }

    // Shift coming up ends a Shift+arrows selection: it becomes the loop region as a mouse drag's release does.
    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (e.Key is Key.LeftShift or Key.RightShift) RaiseSelectionReleased();
    }
}
