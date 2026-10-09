namespace TabForge.Services;

// Owns: the Band view preferences and the names of their choices.
// Does not own: the Preferences rows (SettingsCatalog.Timeline) or applying them (BandViewController).
// Tests: TestBandSettings.
/// <summary>How the Band view looks and follows; stored with the timeline settings.</summary>
public sealed class BandSettings
{
    /// <summary>Instrument size in a row: the full neck (or full keyboard), the 12-fret window, or a small keyboard.</summary>
    public string InstrumentSize { get; set; } = BandChoices.FullNeck;
    /// <summary>What a lane shows: Tab, Notation or Both.</summary>
    public string LaneContent { get; set; } = BandChoices.Tab;
    /// <summary>The lanes follow the playhead like the score (the Follow settings). Null in a file from before this setting: it follows the score unless smooth follow was turned off.</summary>
    public bool? FollowLikeScore { get; set; }
    /// <summary>The Band's own follow when it does not follow the score: true slides the lanes with the playhead, false turns a page at a time.</summary>
    public bool SmoothFollow { get; set; } = true;
    /// <summary>True when the lanes use the score's follow settings.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool FollowsScore => FollowLikeScore ?? SmoothFollow;
    /// <summary>Lane layout: Vertical (systems wrap and stack down the lane) or Horizontal (one line slid sideways).</summary>
    public string LaneLayout { get; set; } = BandChoices.Vertical;
    /// <summary>Rows on the screen at once for a song that has none saved (1 to 5).</summary>
    public int RowsPerScreen { get; set; } = 3;
    /// <summary>The instrument's width in every row, in pixels; 0 = automatic (about a third of the row).</summary>
    public double InstrumentWidth { get; set; }
    /// <summary>True: dragging the grip sets only that row's instrument width (kept with the song); false: one width for all rows.</summary>
    public bool WidthPerRow { get; set; }
    /// <summary>How far the green playhead line reaches in a lane: only across the tab/staff, or the full row height.</summary>
    public string PlayheadLine { get; set; } = BandChoices.TabOnly;
    /// <summary>The Band lanes' own zoom, a multiplier on the shared lane zoom (1 = the score's size).</summary>
    public double LaneZoom { get; set; } = 1;
    /// <summary>Reordering the Band rows or the timeline's tracks reorders both.</summary>
    public bool KeepOrderInSync { get; set; }
}

public static class BandChoices
{
    public const string FullNeck = "Full neck";
    public const string TwelveFrets = "12 frets";
    public const string SmallKeyboard = "Small keyboard";
    public const string Tab = "Tab";
    public const string Notation = "Notation";
    public const string Both = "Both";
    public const string Vertical = "Vertical";
    public const string Horizontal = "Horizontal";
    public static readonly string[] Layouts = { Vertical, Horizontal };
    public const double MinLaneZoom = 0.5, MaxLaneZoom = 3, LaneZoomStep = 1.1;
    public static double ClampZoom(double z) => double.IsFinite(z) ? Math.Round(Math.Clamp(z, MinLaneZoom, MaxLaneZoom), 3) : 1;
    public const string TabOnly = "Tab only";
    public const string FullRow = "Full row";
    public static readonly string[] PlayheadLines = { TabOnly, FullRow };
    public static string NormalizePlayhead(string? v) => PlayheadLines.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) ?? TabOnly;
    /// <summary>Instrument width limits (pixels).</summary>
    public const double MinInstrumentWidth = 120, MaxInstrumentWidth = 1200;
    public static double ClampWidth(double w) => double.IsFinite(w) ? Math.Clamp(w, MinInstrumentWidth, MaxInstrumentWidth) : 0;
    public static readonly string[] Sizes = { FullNeck, TwelveFrets, SmallKeyboard };
    public static readonly string[] Contents = { Tab, Notation, Both };
    /// <summary>Keys on the small keyboard.</summary>
    public const int SmallKeys = 37;

    public static string NormalizeSize(string? v) => Sizes.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) ?? FullNeck;
    public static string NormalizeContent(string? v) => Contents.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) ?? Tab;
    public static string NormalizeLayout(string? v) => Layouts.FirstOrDefault(s => string.Equals(s, v, StringComparison.OrdinalIgnoreCase)) ?? Vertical;
    public static string Next(string[] all, string current) => all[(Array.IndexOf(all, current) + 1) % all.Length];
}
