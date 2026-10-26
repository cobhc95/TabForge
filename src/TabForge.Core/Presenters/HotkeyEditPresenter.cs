using TabForge.Services;

// Owns: the Shortcuts page rules that need no toolkit: which key presses are only modifiers or cancel, the gesture text of a
//       captured key, conflicts across both slots of every command in the same context, reassign, clear, reset of one command,
//       the "customized" mark, and writing the binding into HotkeySettings.
// Does not own: key events, buttons or rebuilding the page (HotkeyPage in the app, PreferencesSpecialPages in the Avalonia app),
//       the preset box, the settings-changed notification (the host raises it after each call that returns true or Assigned).
// Tests: none dedicated, TabForge.Avalonia.Tests.PreferencesPagesTests.
namespace TabForge.Presenters;

/// <summary>What a key press did to a shortcut capture.</summary>
public enum HotkeyKeyOutcome { NotRecording, Waiting, Cancelled, Conflict, Assigned }

public sealed class HotkeyEditPresenter
{
    public const string PressPrompt = "Press a key combination. Esc cancels.";
    private static readonly HashSet<string> ModifierKeys = new(StringComparer.Ordinal)
        { "LeftCtrl", "RightCtrl", "LeftShift", "RightShift", "LeftAlt", "RightAlt", "LWin", "RWin", "System", "None" };

    private readonly HotkeySettings _hotkeys;
    private (string ActionId, int Slot, string Gesture, string ConflictingActionId, int ConflictingSlot)? _pendingConflict;

    public HotkeyEditPresenter(HotkeySettings hotkeys) => _hotkeys = hotkeys;

    public string? RecordingActionId { get; private set; }
    public int RecordingSlot { get; private set; } = 1;

    /// <summary>The conflict text after <see cref="HotkeyKeyOutcome.Conflict"/>, or the status line after <see cref="HotkeyKeyOutcome.Assigned"/> and <see cref="Reassign"/>.</summary>
    public string Message { get; private set; } = "";

    /// <summary>Starts capturing a key for one slot of a command; drops an earlier capture and any pending conflict.</summary>
    public void Begin(string actionId, int slot)
    {
        Cancel();
        _pendingConflict = null;
        RecordingActionId = actionId;
        RecordingSlot = slot;
    }

    public void Cancel() => RecordingActionId = null;

    /// <summary>Feeds the key (the catalogue key name, as in a gesture) with its modifiers to the running capture.</summary>
    public HotkeyKeyOutcome KeyPressed(string key, HotkeyModifiers modifiers)
    {
        if (RecordingActionId is not { } actionId) return HotkeyKeyOutcome.NotRecording;
        if (key == "Escape")
        {
            Cancel();
            return HotkeyKeyOutcome.Cancelled;
        }
        if (ModifierKeys.Contains(key)) return HotkeyKeyOutcome.Waiting;

        var slot = RecordingSlot;
        var gesture = HotkeyCatalog.Format(key, modifiers);
        RecordingActionId = null;
        // A conflict is any other slot of any command in the same context (and the other slot of this command) that already uses the key.
        if (FindConflict(actionId, slot, gesture) is { } found)
        {
            var (other, otherSlot) = found;
            _pendingConflict = (actionId, slot, gesture, other.Id, otherSlot);
            Message = other.Id == actionId
                ? $"Already this command's Hotkey {otherSlot}."
                : $"Already used by {other.Name} ({other.Category}), Hotkey {otherSlot}.";
            return HotkeyKeyOutcome.Conflict;
        }
        Assign(actionId, slot, gesture, null, 1);
        return HotkeyKeyOutcome.Assigned;
    }

    /// <summary>Moves the held-back key from the conflicting command to <paramref name="actionId"/>; false when nothing is pending for it.</summary>
    public bool Reassign(string actionId)
    {
        if (_pendingConflict is not { } pending || pending.ActionId != actionId) return false;
        if (HotkeyCatalog.ById(pending.ConflictingActionId) is not { } conflicting) return false;
        Assign(actionId, pending.Slot, pending.Gesture, conflicting, pending.ConflictingSlot);
        return true;
    }

    public void Clear(string actionId, int slot) => _hotkeys.Disable(actionId, slot);

    public void Reset(string actionId) => _hotkeys.Reset(actionId);

    public bool IsCustomized(HotkeyAction action) => IsSlotCustomized(action, 1) || IsSlotCustomized(action, 2);

    private bool IsSlotCustomized(HotkeyAction action, int slot) => _hotkeys.IsDisabled(action.Id, slot) ||
        !string.Equals(HotkeyCatalog.Canonical(HotkeyCatalog.GestureFor(_hotkeys, action.Id, slot)),
            HotkeyCatalog.Canonical(HotkeyCatalog.DefaultFor(action, slot)), StringComparison.OrdinalIgnoreCase);

    private (HotkeyAction Action, int Slot)? FindConflict(string actionId, int slot, string gesture)
    {
        var wanted = HotkeyCatalog.Canonical(gesture);
        foreach (var other in HotkeyCatalog.All)
        {
            if (!HotkeyCatalog.SameContext(other.Id, actionId)) continue;
            foreach (var otherSlot in new[] { 1, 2 })
            {
                if (other.Id == actionId && otherSlot == slot) continue;
                var existing = HotkeyCatalog.GestureFor(_hotkeys, other.Id, otherSlot);
                if (!string.IsNullOrWhiteSpace(existing) &&
                    string.Equals(HotkeyCatalog.Canonical(existing), wanted, StringComparison.OrdinalIgnoreCase))
                    return (other, otherSlot);
            }
        }
        return null;
    }

    private void Assign(string actionId, int slot, string gesture, HotkeyAction? replaced, int replacedSlot)
    {
        if (replaced is not null) _hotkeys.Disable(replaced.Id, replacedSlot);
        _hotkeys.Set(actionId, slot, gesture);
        _pendingConflict = null;
        var name = HotkeyCatalog.ById(actionId)?.Name ?? actionId;
        Message = replaced is null
            ? $"Bound {name} to {HotkeyCatalog.Display(gesture)}"
            : $"Moved {HotkeyCatalog.Display(gesture)} from {replaced.Name} to {name}";
    }
}
