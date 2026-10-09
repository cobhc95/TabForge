using System.Text.Json.Serialization;
using TabForge.Docking;
using TabForge.Documents;

namespace TabForge.Services;

// Owns: the timeline and section preferences data and the name lists of the fretboard and playhead styles.
// Does not own: loading, saving and validating (AppSettingsStore, SettingsValidator) or the Preferences rows (SettingsCatalog).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
/// <summary>Timeline and section presentation/interaction preferences.</summary>
public sealed class TimelineSettings
{
    /// <summary>Snap settings for moving and trimming clips.</summary>
    public SnapSettings Snap { get; set; } = new();
    /// <summary>Clicking the tuning-fork icon opens the global tuning window (otherwise the type-in box).</summary>
    public bool TuningIconOpensWindow { get; set; } = true;
    public bool ShowSectionNames { get; set; } = true;
    public bool ShowBarNumbers { get; set; } = true;
    public bool SectionDragAnimation { get; set; } = true;
    /// <summary>Show the [ ] brackets around the current section on the timeline.</summary>
    public bool ShowSectionBrackets { get; set; } = true;
    /// <summary>Give sections with the same base name ("Verse 1" / "Verse 2") the same colour.</summary>
    public bool MatchSimilarSectionColours { get; set; } = true;
    /// <summary>Order of the arrangement track-control columns (null = default order).</summary>
    public List<string>? TrackColumnOrder { get; set; }
    /// <summary>Track columns hidden from the track list (only "volume" and "pan" can be hidden; set in the Mixer).</summary>
    public List<string> HiddenTrackColumns { get; set; } = new();
    /// <summary>Widths of the fixed-width track-control columns by id (null = defaults).</summary>
    public Dictionary<string, double>? TrackColumnWidths { get; set; }
    /// <summary>Width of the whole track-controls area; 0 = default.</summary>
    public double TrackControlsWidth { get; set; }
    /// <summary>Resize the track list / arrangement panel to fit all rows when tracks or groups change.</summary>
    public bool AutoFitTrackList { get; set; } = true;
    /// <summary>Height of a track row in the track list and timeline (px). 30 is the default and the minimum; dragging the track list's splitter stretches it (up to 90) while auto-fit is on.</summary>
    public double TrackRowHeight { get; set; } = 34;
    /// <summary>Height (px) the user dragged the track list shorter than its rows; 0 = fit to the rows.</summary>
    public double TrackListHeight { get; set; }
    /// <summary>Timeline appearance: draw each note as a mark in its lane (off = the lane's default drawing).</summary>
    public bool ShowIndividualNotes { get; set; }
    /// <summary>Timeline appearance: draw each lane as one continuous line (turns <see cref="ShowIndividualNotes"/> off).</summary>
    public bool ShowContinuousLine { get; set; }
    /// <summary>Timeline appearance: leave out the grid lines in bars without notes.</summary>
    public bool HideEmptyGrid { get; set; } = true;
    /// <summary>Horizontal lines between track rows in the timeline and the track list. Off by default: rows are told apart by their tints and a faint gap.</summary>
    public bool ShowTrackLines { get; set; }
    /// <summary>Timeline appearance: a faint glow on bars that have notes.</summary>
    public bool BarGlow { get; set; } = true;
    /// <summary>How the playback position is shown on the timeline (see <see cref="PlayheadStyles"/>).</summary>
    public string PlayheadStyle { get; set; } = PlayheadStyles.Line;
    /// <summary>New songs list their tracks under a header per mixer group.</summary>
    public bool ShowGroupsInNewSongs { get; set; }
    /// <summary>A clip lane with no clips left (after a move, delete, cut) is removed and the lanes below close up. Armed tracks are never touched.</summary>
    public bool AutoRemoveEmptyLanes { get; set; } = true;
    /// <summary>The "Add track" strip under the last track (track list and timeline). The + Track button stays either way.</summary>
    public bool ShowAddTrackLane { get; set; } = true;
    /// <summary>The Band view's preferences.</summary>
    public BandSettings Band { get; set; } = new();
}

public static class InstrumentViews
{
    public const string Fretboard = "Fretboard";
    public const string Keyboard = "Keyboard";
    public const string Drums = "Drums";
    public const string MatchInstrument = "Match the instrument";
    public static readonly string[] All = { MatchInstrument, Fretboard, Keyboard, Drums };
    public static readonly int[] KeyboardSizes = { 88, 76, 61, 49, 37, 25 };
}

public static class ScaleHighlightStyles
{
    public const string Shaded = "Shaded";
    public const string Circles = "Circles";
    public const string Rings = "Rings";
    public static readonly string[] All = { Shaded, Circles, Rings };
    /// <summary>Colour names; the colours themselves are <see cref="ThemeService.ScaleHighlightColour"/> (the settings types stay WPF-free).</summary>
    public static readonly string[] Colours = { "Blue", "Green", "Amber", "Purple", "Red", "Teal", "Grey" };
    public const int MinStrength = 10, MaxStrength = 150, DefaultStrength = 100, StrengthStep = 10;
    /// <summary>The strength setting as a multiplier of the standard look (1.0 = today's alphas).</summary>
    public static double StrengthFactor(int percent) => Math.Clamp(percent, MinStrength, MaxStrength) / 100.0;
    /// <summary>A scale mark's alpha: the standard alpha times the strength; other scale notes top out at 0.9 so the root (up to 1.0) always stays stronger.</summary>
    public static double ScaleAlpha(double standardAlpha, double strength, bool root) => root ? Math.Min(1, standardAlpha * strength) : Math.Min(0.9, standardAlpha * strength);
}

public static class FretMarkerLevels
{
    public const string Original = "Original";
    public const string Brighter = "Brighter";
    public static readonly string[] All = { Original, Brighter, "Bright", "Brightest" };
    public static readonly string[] Colours = { "Default", "White", "Silver", "Amber", "Blue", "Green" };

    /// <summary>0 = the original dim dots, rising to 1 = brightest.</summary>
    public static double Level(string? level) => level switch { "Original" => 0, "Bright" => 0.6, "Brightest" => 1, _ => 0.3 };
    // The colours for Colours are ThemeService.FretMarkerColour.
}

public static class FretNumberSizes
{
    public const string Small = "Small";
    public const string Medium = "Medium";
    public const string Large = "Large";
    public static readonly string[] All = { Small, Medium, Large };
    public static string Label(string size) => size switch { Small => "Small (75%)", Large => "Large (100%, default)", _ => "Medium (85%)" };
    public static double Scale(string? size) => size switch { Small => 0.75, Large => 1.0, _ => 0.85 };
}

/// <summary>How far apart the fretboard strings may be drawn: a cap on the string gap relative to the fret width, so a tall or
/// narrow pane never stretches the board (see <see cref="TabForge.Visualization.FretboardGeometry"/>).</summary>
public static class FretStringSpacings
{
    public const string Compact = "Compact";
    public const string Natural = "Natural";
    public const string Wide = "Wide";
    public static readonly string[] All = { Compact, Natural, Wide };
    public static string Label(string v) => v switch { Compact => "Compact (75%)", Wide => "Wide (150%, the maximum)", _ => "Natural (default)" };
    /// <summary>Multiplier of the natural string-gap cap; never above 1.5.</summary>
    public static double Factor(string? v) => v switch { Compact => 0.75, Wide => 1.5, _ => 1.0 };
    public static string Next(string? v) => v switch { Compact => Natural, Natural => Wide, _ => Compact };
}

/// <summary>Playback position marker on the timeline: the vertical line, a marker inside the current bar cell, or both.</summary>
public static class PlayheadStyles
{
    public const string Line = "Line";
    public const string BarMarker = "Bar marker";
    public const string Both = "Both";
    public static readonly string[] All = { Line, BarMarker, Both };
    public static string Normalize(string? v) => All.FirstOrDefault(a => string.Equals(a, v, StringComparison.OrdinalIgnoreCase)) ?? Line;
    public static string Next(string? v) => Normalize(v) switch { Line => BarMarker, BarMarker => Both, _ => Line };
    public static bool ShowsLine(string? v) => Normalize(v) != BarMarker;
    public static bool ShowsBarMarker(string? v) => Normalize(v) != Line;
}

public static class KeyboardKeyStyles
{
    public const string MatchTheme = "Match the theme";
    public const string Grey = "Grey keys";
    public const string White = "White keys";
    public static readonly string[] All = { MatchTheme, Grey, White };
}
