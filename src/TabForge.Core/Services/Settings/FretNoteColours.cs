using System;
using System.Collections.Generic;
using TabForge.Models;
using TabForge.Platform.Theme;

namespace TabForge.Services;

/// <summary>The playing roles whose fretboard dot colour the person can choose.</summary>
public enum FretNoteRole { Now, Next, Upcoming }

// Owns: the fretboard note colours' one master table: each role's name, the theme palette keys its default comes from (now green, next amber,
//   upcoming blue, per theme), the rule that a stored colour is empty while it is the default (so the theme still decides), reading and writing
//   the three EditingSettings values by role, and the dot styles (Fill, Ring).
// Does not own: the drawing (Avalonia FretboardPlaybackMarkers, InstrumentPanelView), the colour picker (ColourButton) or loading and
//   validating settings (SettingsValidator).
// Tests: TabForge.Avalonia.Tests.FretNoteColoursTests.
public static class FretNoteColours
{
    /// <summary>The dot styles: a filled dot (the default, the look before the setting existed) or a ring in the role's colour.</summary>
    public const string Fill = "Fill", Ring = "Ring";
    public static readonly IReadOnlyList<string> DotStyles = new[] { Fill, Ring };

    public static readonly IReadOnlyList<FretNoteRole> Roles = new[] { FretNoteRole.Now, FretNoteRole.Next, FretNoteRole.Upcoming };

    /// <summary>The role's name as the legend and the settings card show it.</summary>
    public static string Name(FretNoteRole role) => role switch { FretNoteRole.Now => "Now", FretNoteRole.Next => "Next", _ => "Upcoming" };

    /// <summary>The palette key of the role's default colour in the theme (the light theme's now dot has its own green).</summary>
    public static string PaletteKey(FretNoteRole role, bool light) => role switch
    {
        FretNoteRole.Now => light ? "FretNow" : "InstrumentCurrent",
        FretNoteRole.Next => "InstrumentNext",
        _ => "InstrumentAccent",
    };

    /// <summary>The role's default colour in the theme, as "#RRGGBB".</summary>
    public static string DefaultHex(FretNoteRole role, bool light) => Palette.Hex(PaletteKey(role, light), light);

    /// <summary>
    /// The value to store for a picked colour: empty when it is not a colour or is the role's default in either theme (the theme keeps
    /// deciding), else the colour as "#RRGGBB".
    /// </summary>
    public static string Stored(FretNoteRole role, string? picked)
    {
        if (!ColourHex.TryParse(picked, out var colour)) return "";
        var hex = ColourHex.Format(colour.R, colour.G, colour.B);
        return IsDefaultInEitherTheme(role, hex) ? "" : hex;
    }

    // A default of either theme (dark, then light) stays empty, so a later theme switch still picks that theme's colour.
    private static bool IsDefaultInEitherTheme(FretNoteRole role, string hex)
    {
        for (var theme = 0; theme < 2; theme++)
            if (string.Equals(hex, DefaultHex(role, theme == 1), StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>The colour shown for the role: the stored one, else the theme's default.</summary>
    public static string Shown(FretNoteRole role, string? stored, bool light) =>
        ColourHex.TryParse(stored, out var colour) ? ColourHex.Format(colour.R, colour.G, colour.B) : DefaultHex(role, light);

    /// <summary>The stored colour of a role in the settings (empty = the theme's default).</summary>
    public static string Get(EditingSettings editing, FretNoteRole role) => role switch
    {
        FretNoteRole.Now => editing.FretNowColour,
        FretNoteRole.Next => editing.FretNextColour,
        _ => editing.FretUpcomingColour,
    };

    /// <summary>Stores a picked colour for a role (through <see cref="Stored"/>).</summary>
    public static void Set(EditingSettings editing, FretNoteRole role, string? picked)
    {
        var value = Stored(role, picked);
        switch (role)
        {
            case FretNoteRole.Now: editing.FretNowColour = value; break;
            case FretNoteRole.Next: editing.FretNextColour = value; break;
            default: editing.FretUpcomingColour = value; break;
        }
    }

    /// <summary>The dot style, or Fill for anything else (an older or hand-edited file).</summary>
    public static string NormalizeStyle(string? style) => string.Equals(style, Ring, StringComparison.OrdinalIgnoreCase) ? Ring : Fill;
}
