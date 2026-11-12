using System;
using System.Collections.Generic;
using System.Linq;

namespace TabForge.Services;

/// <summary>How an amount is counted.</summary>
public enum FocusBoostMode
{
    /// <summary>Decibels (6 is about twice the level).</summary>
    Decibels,
    /// <summary>A percentage of the track's own level (50 is one and a half times when raising, half when lowering).</summary>
    Percent,
    /// <summary>One fixed level, 0 to 127, whatever the fader says (when lowering, a cap: a track already under it is left as it is).</summary>
    ToLevel,
}

// Owns: one Focus Track amount: the mode and amount, how they raise or lower a track's 0-127 level, and the limits each mode keeps.
// Does not own: which tracks it applies to (FocusTrackSettings), the factors Focus Track works out (PracticeFocus).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusBoost
{
    public const double MaxDecibels = 24, MaxPercent = 400, MaxLevel = 127;

    public FocusBoostMode Mode { get; set; } = FocusBoostMode.Decibels;
    public double Amount { get; set; } = 6;

    public FocusBoost() { }
    public FocusBoost(FocusBoostMode mode, double amount) { Mode = mode; Amount = amount; }

    public FocusBoost Clone() => new(Mode, Amount);

    /// <summary>The amount kept inside the mode's limits for a raise (To level is a 0-127 level).</summary>
    public double ClampedAmount => Mode switch
    {
        FocusBoostMode.Decibels => Math.Clamp(Amount, 0, MaxDecibels),
        FocusBoostMode.Percent => Math.Clamp(Amount, 0, MaxPercent),
        _ => Math.Clamp(Amount, 0, MaxLevel),
    };

    /// <summary>The amount kept inside the limits of a lowering (a percentage is at most 100: silence).</summary>
    public double ClampedLowerAmount => Mode switch
    {
        FocusBoostMode.Decibels => Math.Clamp(Amount, 0, MaxDecibels),
        FocusBoostMode.Percent => Math.Clamp(Amount, 0, 100),
        _ => Math.Clamp(Amount, 0, MaxLevel),
    };

    /// <summary>The level a track plays at when raised, from its own 0-127 level (not yet kept under 127).</summary>
    public double Apply(int level) => Mode switch
    {
        FocusBoostMode.Decibels => level * Math.Pow(10, ClampedAmount / 20),
        FocusBoostMode.Percent => level * (1 + ClampedAmount / 100),
        _ => ClampedAmount,
    };

    /// <summary>The level a track plays at when lowered, from its own 0-127 level: lowered by the amount, or kept under a level (never raised).</summary>
    public double ApplyLower(int level) => Mode switch
    {
        FocusBoostMode.Decibels => level * Math.Pow(10, -ClampedLowerAmount / 20),
        FocusBoostMode.Percent => level * (1 - ClampedLowerAmount / 100),
        _ => Math.Min(level, ClampedLowerAmount),
    };
}

/// <summary>What happens to the tracks of a mixer group other than the selected one while Focus Track is on.</summary>
public enum FocusOtherMode
{
    /// <summary>Lowered by the same amount the selected track is raised (the default).</summary>
    SameAsBoost,
    /// <summary>Left exactly as the fader says.</summary>
    LeaveAlone,
    /// <summary>Lowered by an amount of its own.</summary>
    Lower,
    /// <summary>Raised by an amount of its own, as the selected track is.</summary>
    Raise,
}

// Owns: what Focus Track does to the other tracks of one mixer group: the behaviour and, for Lower and Raise, its amount.
// Does not own: the selected track's boost (FocusTrackSettings.Boost) or the levels (PracticeFocus).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusOther
{
    public FocusOtherMode Mode { get; set; } = FocusOtherMode.SameAsBoost;

    /// <summary>The amount a Lower or Raise uses; ignored by the other behaviours.</summary>
    public FocusBoost Amount { get; set; } = new(FocusBoostMode.Decibels, 3);

    public FocusOther() { }
    public FocusOther(FocusOtherMode mode, FocusBoost? amount = null) { Mode = mode; Amount = amount ?? new FocusBoost(FocusBoostMode.Decibels, 3); }

    public FocusOther Clone() => new(Mode, Amount?.Clone());
}

// Owns: Focus Track's saved behaviour as one template: how much louder the selected track goes, and for each mixer group (Guitars, Basses, Drums, Other
//   instruments) what happens to its other tracks (lowered like the boost, left alone, lowered or raised by an amount of their own); the ready-made templates
//   and the name of the one in use ("Custom" once a row is changed by hand).
// Does not own: the on / off (AudioSettings.FocusTrack), the levels (PracticeFocus), the settings window (FocusTrackSettingsWindow).
// Tests: TabForge.Avalonia.Tests.PracticeFocusTests.
public sealed class FocusTrackSettings
{
    public const string Guitars = "Guitars", Basses = "Basses", Drums = "Drums", OtherInstruments = "Other instruments";

    /// <summary>The groups the template covers, in the order the window lists them.</summary>
    public static readonly IReadOnlyList<string> Groups = new[] { Guitars, Basses, Drums, OtherInstruments };

    public const string Spotlight = "Spotlight", KeepRhythm = "Keep the rhythm section", OnlyBoost = "Only boost the selected track", RhythmUp = "Rhythm section up", Custom = "Custom";

    /// <summary>The ready-made templates, then Custom.</summary>
    public static readonly IReadOnlyList<string> Presets = new[] { Spotlight, KeepRhythm, OnlyBoost, RhythmUp, Custom };

    /// <summary>How much louder the selected track goes (20% until the user changes it).</summary>
    public FocusBoost Boost { get; set; } = new(FocusBoostMode.Percent, DefaultPercent);

    /// <summary>The boost a fresh install and Reset start from.</summary>
    public const double DefaultPercent = 20;

    /// <summary>The name of the template in use; Custom when a group was changed by hand.</summary>
    public string Preset { get; set; } = Spotlight;

    /// <summary>What happens to the other tracks of each group, by mixer group name; a group not listed is lowered like the boost.</summary>
    public Dictionary<string, FocusOther> Others { get; set; } = ForPreset(Spotlight);

    /// <summary>The behaviour for the other tracks of <paramref name="group"/> (a group of the user's own is treated as Other instruments).</summary>
    public FocusOther OtherFor(string group) =>
        Others.TryGetValue(group, out var own) && own is not null ? own
        : Others.TryGetValue(OtherInstruments, out var rest) && rest is not null ? rest
        : new FocusOther();

    /// <summary>The behaviours a ready-made template stands for (Custom: every group lowered like the boost).</summary>
    public static Dictionary<string, FocusOther> ForPreset(string preset)
    {
        FocusOther Same() => new(FocusOtherMode.SameAsBoost);
        FocusOther Leave() => new(FocusOtherMode.LeaveAlone);
        FocusOther Up() => new(FocusOtherMode.Raise, new FocusBoost(FocusBoostMode.Decibels, 3));
        return preset switch
        {
            KeepRhythm => new() { [Guitars] = Same(), [Basses] = Leave(), [Drums] = Leave(), [OtherInstruments] = Same() },
            OnlyBoost => Groups.ToDictionary(g => g, _ => Leave()),
            RhythmUp => new() { [Guitars] = Same(), [Basses] = Up(), [Drums] = Up(), [OtherInstruments] = Same() },
            _ => Groups.ToDictionary(g => g, _ => Same()),
        };
    }

    /// <summary>A short description of a template, for the window.</summary>
    public static string Describe(string preset) => preset switch
    {
        Spotlight => "Every other track is lowered by the amount the selected track is raised.",
        KeepRhythm => "Drums and bass stay as they are; the other tracks are lowered.",
        OnlyBoost => "Only the selected track changes; nothing else is lowered.",
        RhythmUp => "Drums and bass are raised a little too; the other tracks are lowered.",
        _ => "Your own choices for each group.",
    };
}
