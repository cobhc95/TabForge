using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Models;

namespace TabForge.Views.Score;

// Owns: the appearance change kinds (layout or repaint) and the host interface that reacts to them.
// Does not own: the settings themselves (Services) or the drawing they change.
// Tests: none named.

/// <summary>What an appearance setting changed, so the editor can lay the score out again or only repaint it.</summary>
internal enum ScoreAppearanceChange { Layout, Repaint }

/// <summary>The editor that owns an appearance and reacts to its changes.</summary>
internal interface IScoreAppearanceHost
{
    void AppearanceChanged(ScoreAppearanceChange change);
}

/// <summary>
/// How a score looks: paper, ink and highlight colours, spacing, which labels show and the playing-bar band. The settings applier writes it;
/// the layout and the drawing read it. A spacing or label setting that changes the layout, and a playing-bar setting that changes only the
/// picture, tell the editor through <see cref="IScoreAppearanceHost"/>; the colour and intensity settings are read on the next repaint.
/// </summary>
internal sealed class ScoreAppearance : IScoreLayoutSettings
{
    private readonly IScoreAppearanceHost _host;
    private double _scoreSpacing = 1.0;
    private double _systemVerticalSpacing = 1.0;
    private double _measureHorizontalSpacing = 1.0;
    private bool _centerSystems;
    private bool _showDynamics = true;
    private bool _barEnabled = true;
    private UiColor _barColor = Palette.Get("FollowPlayingBar");
    private UiColor _playbackColor = Palette.Get("FollowHighlightText");
    private UiColor _durationGlowColor = Palette.Get("FollowDurationGlow");
    private UiColor _highlightBackground = Palette.Get("FollowHighlightBackground");
    private double _barOpacity = 0.10;
    private bool _barWhenStopped;

    internal ScoreAppearance(IScoreAppearanceHost host) => _host = host;

    public LedgerLineMode LedgerLines { get; set; } = LedgerLineMode.Minimal;
    public bool DarkPaper { get; set; } = true;
    public UiColor DarkPaperColor { get; set; } = Palette.Get("ScoreDarkPaperDefault");
    public UiColor LightPaperColor { get; set; } = Palette.Get("ScoreLightPaperDefault");
    public UiColor DarkInkColor { get; set; } = Palette.Get("ScoreDarkInkDefault");
    public UiColor LightInkColor { get; set; } = Palette.Get("ScoreLightInkDefault");
    public UiColor DarkStaffLineColor { get; set; } = Palette.Get("ScoreDarkStaffLineDefault");
    public UiColor LightStaffLineColor { get; set; } = Palette.Get("ScoreLightStaffLineDefault");
    /// <summary>The paper, ink and staff-line colours of the paper in use.</summary>
    public UiColor PaperColor => DarkPaper ? DarkPaperColor : LightPaperColor;
    public UiColor InkColor => DarkPaper ? DarkInkColor : LightInkColor;
    public UiColor StaffLineColor => DarkPaper ? DarkStaffLineColor : LightStaffLineColor;
    public UiColor AccentColor { get; set; } = Palette.Get("DefaultAccent");
    /// <summary>Red of a bar whose rhythm does not add up (number, overfull staff lines): at least 4.5:1 on either paper.</summary>
    public UiColor LoopTintColor => Palette.Get("LoopRegion", light: !DarkPaper);
    public UiColor ErrorColor => Palette.Get("ScoreErrorRed", light: !DarkPaper);
    public UiColor CursorColor { get; set; } = Palette.Get("DefaultCursor");
    /// <summary>A playback colour the user left at the theme's default follows the paper: the token's light value on the light paper.</summary>
    private UiColor Themed(UiColor chosen, string token) => chosen == Palette.Get(token) ? Palette.Get(token, light: !DarkPaper) : chosen;
    /// <summary>Colour of sounding notes, fret numbers and the playhead.</summary>
    public UiColor PlaybackColor { get => Themed(_playbackColor, "FollowHighlightText"); set => _playbackColor = value; }
    public UiColor DurationGlowColor { get => Themed(_durationGlowColor, "FollowDurationGlow"); set => _durationGlowColor = value; }
    public double DurationGlowOpacity { get; set; } = 0;
    /// <summary>Stroke width of the vertical playback line in the score, in page units.</summary>
    public double PlayheadThickness { get; set; } = 2;
    /// <summary>True: the playing line spans the staff and the tab whatever the notation mode (a Band lane's "Full row"); false: only the engraved rows.</summary>
    public bool PlayheadFullRow { get; set; }
    /// <summary>Background tint behind the beat that is sounding.</summary>
    public UiColor HighlightBackground { get => Themed(_highlightBackground, "FollowHighlightBackground"); set => _highlightBackground = value; }
    /// <summary>Draw the sounding-beat band at all.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    public bool ShowSectionHeadings { get; set; } = true;
    public bool ShowBarNumbers { get; set; } = true;
    public int BarNumberFrequency { get; set; } = 1;
    public double HoverHighlightIntensity { get; set; } = 0.27;
    /// <summary>Draw the thin outline around the hovered cell, sized to the note length.</summary>
    public bool ShowNoteOutline { get; set; } = true;
    /// <summary>Engrave the whole-bar rest in a bar that holds nothing (and the rest fill's rests); off draws such a bar empty, as the reference does.</summary>
    public bool DrawEmptyBarRests { get; set; }
    public double SelectionHighlightIntensity { get; set; } = 0.25;
    public UiColor SelectionColor { get; set; } = Palette.Get("DefaultSelection");
    public UiColor HoverColor { get; set; } = Palette.Get("DefaultHover");

    /// <summary>Readability scale for the tablature: line spacing and fret numbers.</summary>
    public double ScoreSpacing
    {
        get => _scoreSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.85, 1.6);
            if (Math.Abs(clamped - _scoreSpacing) < 0.001) return;
            _scoreSpacing = clamped;
            _host.AppearanceChanged(ScoreAppearanceChange.Layout);
        }
    }

    public double SystemVerticalSpacing
    {
        get => _systemVerticalSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.7, 1.6);
            if (Math.Abs(clamped - _systemVerticalSpacing) < 0.001) return;
            _systemVerticalSpacing = clamped;
            _host.AppearanceChanged(ScoreAppearanceChange.Layout);
        }
    }

    public double MeasureHorizontalSpacing
    {
        get => _measureHorizontalSpacing;
        set
        {
            var clamped = Math.Clamp(value, 0.8, 1.6);
            if (Math.Abs(clamped - _measureHorizontalSpacing) < 0.001) return;
            _measureHorizontalSpacing = clamped;
            _host.AppearanceChanged(ScoreAppearanceChange.Layout);
        }
    }

    /// <summary>Continuous, viewport-reflowing score mode (systems still start at the left margin).</summary>
    public bool CenterSystems
    {
        get => _centerSystems;
        set
        {
            if (_centerSystems == value) return;
            _centerSystems = value;
            _host.AppearanceChanged(ScoreAppearanceChange.Layout);
        }
    }

    /// <summary>Engrave dynamics markings (Preferences &gt; Score &gt; Labels).</summary>
    public bool ShowDynamics
    {
        get => _showDynamics;
        set
        {
            if (_showDynamics == value) return;
            _showDynamics = value;
            _host.AppearanceChanged(ScoreAppearanceChange.Layout);
        }
    }

    /// <summary>Show a translucent band over the bar that is playing. On by default.</summary>
    public bool PlayingBarEnabled { get => _barEnabled; set { if (_barEnabled == value) return; _barEnabled = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Colour of the playing-bar band (its opacity is applied on top).</summary>
    public UiColor PlayingBarColor { get => Themed(_barColor, "FollowPlayingBar"); set { if (_barColor == value) return; _barColor = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Opacity of the band, 0..1.</summary>
    public double PlayingBarOpacity { get => _barOpacity; set { if (_barOpacity == value) return; _barOpacity = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Also band the edit cursor's bar while playback is stopped.</summary>
    public bool PlayingBarWhenStopped { get => _barWhenStopped; set { if (_barWhenStopped == value) return; _barWhenStopped = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }
}
