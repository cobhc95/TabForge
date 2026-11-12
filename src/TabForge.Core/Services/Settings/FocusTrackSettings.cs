using System;
using System.Collections.Generic;

namespace TabForge.Services;

/// <summary>How a Focus Track rule raises the selected track.</summary>
public enum FocusBoostMode
{
    /// <summary>Louder by a number of decibels (6 is about twice the level).</summary>
    Decibels,
    /// <summary>Louder by a percentage of the track's own level (50 is one and a half times).</summary>
    Percent,
    /// <summary>Played at one fixed level, 0 to 127, whatever the fader says.</summary>
    ToLevel,
}

// Owns: one Focus Track rule: the mode and amount, how they raise a track's 0-127 level, and the limits each mode keeps.
// Does not own: which tracks it applies to (FocusTrackSettings), the headroom handling (PracticeFocus).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusBoost
{
    public const double MaxDecibels = 24, MaxPercent = 400, MaxLevel = 127;

    public FocusBoostMode Mode { get; set; } = FocusBoostMode.Decibels;
    public double Amount { get; set; } = 6;

    public FocusBoost() { }
    public FocusBoost(FocusBoostMode mode, double amount) { Mode = mode; Amount = amount; }

    public FocusBoost Clone() => new(Mode, Amount);

    /// <summary>The amount kept inside the mode's limits (a boost never lowers; To level is a 0-127 level).</summary>
    public double ClampedAmount => Mode switch
    {
        FocusBoostMode.Decibels => Math.Clamp(Amount, 0, MaxDecibels),
        FocusBoostMode.Percent => Math.Clamp(Amount, 0, MaxPercent),
        _ => Math.Clamp(Amount, 0, MaxLevel),
    };

    /// <summary>The level the selected track should play at, from its own 0-127 level (not yet kept under 127).</summary>
    public double Apply(int level) => Mode switch
    {
        FocusBoostMode.Decibels => level * Math.Pow(10, ClampedAmount / 20),
        FocusBoostMode.Percent => level * (1 + ClampedAmount / 100),
        _ => ClampedAmount,
    };
}

// Owns: Focus Track's saved rules: the rule for any selected track, and an optional rule of its own for a mixer group (Guitars, Basses, Drums, Other instruments).
// Does not own: the on / off (AudioSettings.FocusTrack), the levels (PracticeFocus), the settings window (FocusTrackSettingsWindow).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusTrackSettings
{
    /// <summary>The rule for a selected track whose group has none of its own: +6 dB.</summary>
    public FocusBoost Boost { get; set; } = new();

    /// <summary>Rules of their own by mixer group name; a group not listed uses <see cref="Boost"/>.</summary>
    public Dictionary<string, FocusBoost> Groups { get; set; } = new();

    /// <summary>The rule for a selected track in <paramref name="group"/>.</summary>
    public FocusBoost RuleFor(string group) => Groups.TryGetValue(group, out var own) && own is not null ? own : Boost ?? new FocusBoost();
}
