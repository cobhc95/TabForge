using System.Text.Json.Serialization;

namespace TabForge.Services;

// Owns: the persisted hotkey settings (preset name, Hotkey 1 and Hotkey 2 bindings, cleared slots).
// Does not own: the catalogue of commands and their defaults (HotkeyCatalog), loading and validating.
// Tests: TestHotkeyTwoSlots, TestHotkeySettingsMigration.
/// <summary>Action id → key gesture string (e.g. "Ctrl+Shift+T"). Missing ids use the catalog default.</summary>
public sealed class HotkeySettings
{
    /// <summary>Base key layout (see HotkeyPresets); Bindings hold the user's own changes on top.</summary>
    public string Preset { get; set; } = "TabForge";
    public Dictionary<string, string> Bindings { get; set; } = new();
    /// <summary>Commands explicitly unbound by the user; absence from Bindings otherwise means default.</summary>
    public List<string> DisabledActions { get; set; } = new();
    /// <summary>Preferences > Editing > "Reverse + / - duration keys": swaps the default Longer / Shorter keys (set when settings are applied; not stored here).</summary>
    [JsonIgnore]
    public bool ReverseDurationKeys { get; set; }

    [JsonIgnore]
    public string this[string action]
    {
        get => Bindings.TryGetValue(action, out var v) ? v : "";
        set
        {
            DisabledActions.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(value)) Bindings.Remove(action);
            else Bindings[action] = value;
        }
    }

    public bool IsDisabled(string action) => DisabledActions.Contains(action, StringComparer.OrdinalIgnoreCase);
    public void Disable(string action)
    {
        Bindings.Remove(action);
        if (!IsDisabled(action)) DisabledActions.Add(action);
    }

    /// <summary>Hotkey 2 bindings (action id to gesture). Empty in a file written before the second slot existed.</summary>
    public Dictionary<string, string> Bindings2 { get; set; } = new();
    /// <summary>Hotkey 2 slots the user cleared, or that migration cleared so a default never appears behind the user's back.</summary>
    public List<string> DisabledActions2 { get; set; } = new();

    public string Get(string action, int slot) => slot == 2
        ? (Bindings2.TryGetValue(action, out var v) ? v : "")
        : this[action];

    /// <summary>Sets one slot's gesture; an empty gesture returns the slot to its default.</summary>
    public void Set(string action, int slot, string gesture)
    {
        if (slot != 2) { this[action] = gesture; return; }
        DisabledActions2.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrWhiteSpace(gesture)) Bindings2.Remove(action);
        else Bindings2[action] = gesture;
    }

    public bool IsDisabled(string action, int slot) =>
        slot == 2 ? DisabledActions2.Contains(action, StringComparer.OrdinalIgnoreCase) : IsDisabled(action);

    public void Disable(string action, int slot)
    {
        if (slot != 2) { Disable(action); return; }
        Bindings2.Remove(action);
        if (!IsDisabled(action, 2)) DisabledActions2.Add(action);
    }

    /// <summary>Returns one slot to its default.</summary>
    public void Reset(string action, int slot)
    {
        if (slot != 2)
        {
            Bindings.Remove(action);
            DisabledActions.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
            return;
        }
        Bindings2.Remove(action);
        DisabledActions2.RemoveAll(id => id.Equals(action, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Returns both slots of an action to their defaults.</summary>
    public void Reset(string action)
    {
        Reset(action, 1);
        Reset(action, 2);
    }
}
