using System;
using global::Avalonia.Input;

namespace TabForge.Avalonia;

// Owns: which key counts as "the primary modifier" (Ctrl on Windows and Linux, Cmd on macOS): the one place that names KeyModifiers.Control.
// Does not own: the shortcut catalogue or its labels (Core HotkeyCatalog).
// Tests: PortabilityGuardTests (no other file tests KeyModifiers.Control directly).
internal static class PrimaryModifier
{
    private static readonly KeyModifiers Primary = OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control;

    /// <summary>True when the primary modifier (Ctrl; Cmd on macOS) is held.</summary>
    public static bool HasPrimary(this KeyModifiers modifiers) => modifiers.HasFlag(Primary);
}
