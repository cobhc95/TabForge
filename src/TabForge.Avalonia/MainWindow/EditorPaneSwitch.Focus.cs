using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Models;

namespace TabForge.Avalonia;

// The visible editor owns note-entry keys, not the mode pill or transport button last pressed.
// Defer until the input/layout transaction finishes: focusing an invisible/detached score during
// a pane switch silently fails. Never take focus from a text editor or an open modal dialog.
public sealed partial class EditorPaneSwitch
{
    private Window? _focusOwner;
    private bool _focusQueued;
    private bool _claimEditingFocus;

    private void AttachEditingFocus(Window? owner)
    {
        _focusOwner = owner;
        if (owner is null) return;
        owner.Opened += RestoreAfterActivation;
        owner.Activated += RestoreAfterActivation; // includes returning from a modal dialog/file picker
        owner.AddHandler(InputElement.PointerReleasedEvent, RestoreAfterTransport, RoutingStrategies.Bubble, handledEventsToo: true);
        Host.AttachedToVisualTree += (_, _) => RestoreEditingFocus(); // dock re-parenting
        owner.Closed += (_, _) =>
        {
            owner.Opened -= RestoreAfterActivation;
            owner.Activated -= RestoreAfterActivation;
            owner.RemoveHandler(InputElement.PointerReleasedEvent, RestoreAfterTransport);
            _focusOwner = null;
        };
        RestoreEditingFocus();
    }

    private void RestoreAfterActivation(object? sender, System.EventArgs e) => RestoreEditingFocus();

    private void RestoreAfterTransport(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && e.Source is global::Avalonia.Visual source &&
            (source is TransportBarView.TransportButton || source.FindAncestorOfType<TransportBarView.TransportButton>() is not null)) RestoreEditingFocus(claimSelection: true);
    }

    internal void RestoreEditingFocus(bool claimSelection = false)
    {
        _claimEditingFocus |= claimSelection;
        if (_focusOwner is null || _focusQueued) return;
        _focusQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _focusQueued = false;
            var claim = _claimEditingFocus;
            _claimEditingFocus = false;
            if (_focusOwner is not { IsActive: true } owner || MainWindowEditingFocus.IsEditing(owner.FocusManager?.GetFocusedElement())) return;
            // A deliberate timeline selection still owns clipboard/delete keys. Default restoration must
            // not turn a selected track/clip into a score selection (OnGotFocus announces that change).
            if (!claim && _songs.Active.Selection.Focus is not (FocusTarget.None or FocusTarget.ScoreSelection)) return;
            if (_shown.Roll)
            {
                if (PianoRoll.IsEffectivelyVisible) PianoRoll.Surface.Focus();
            }
            else if (_scoreView.IsEffectivelyVisible)
            {
                _scoreView.Focus();
                if (claim) _scoreView.RaiseFocused(); // Focus() alone emits nothing when the control already had focus
            }
        }, DispatcherPriority.Input);
    }
}
