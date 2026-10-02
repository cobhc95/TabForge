using System.Windows.Media;
using TabForge.Models;

namespace TabForge.Views.Score;

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
internal sealed class ScoreAppearance
{
    private readonly IScoreAppearanceHost _host;
    private double _scoreSpacing = 1.0;
    private double _systemVerticalSpacing = 1.0;
    private double _measureHorizontalSpacing = 1.0;
    private bool _centerSystems;
    private bool _showDynamics = true;
    private bool _barEnabled;
    private Color _barColor = Color.FromRgb(0xFF, 0xE0, 0x66);
    private double _barOpacity = 0.20;
    private bool _barWhenStopped;

    internal ScoreAppearance(IScoreAppearanceHost host) => _host = host;

    public LedgerLineMode LedgerLines { get; set; } = LedgerLineMode.Minimal;
    public bool DarkPaper { get; set; } = true;
    public Color DarkPaperColor { get; set; } = Color.FromRgb(0x15, 0x18, 0x1D);
    public Color LightPaperColor { get; set; } = Colors.White;
    public Color DarkInkColor { get; set; } = Color.FromRgb(0xE7, 0xEA, 0xEF);
    public Color LightInkColor { get; set; } = Color.FromRgb(0x11, 0x11, 0x11);
    public Color DarkStaffLineColor { get; set; } = Color.FromRgb(0x34, 0x39, 0x40);
    public Color LightStaffLineColor { get; set; } = Color.FromRgb(0xD5, 0xD5, 0xD5);
    public Color AccentColor { get; set; } = Color.FromRgb(0x4C, 0x9A, 0xFF);
    public Color CursorColor { get; set; } = Color.FromRgb(0xF2, 0xC1, 0x4E);
    /// <summary>Colour of sounding notes, fret numbers and the playhead.</summary>
    public Color PlaybackColor { get; set; } = Color.FromRgb(0x3F, 0xB9, 0x50);
    public Color DurationGlowColor { get; set; } = Color.FromRgb(0x3F, 0xB9, 0x50);
    public double DurationGlowOpacity { get; set; } = 0;
    /// <summary>Background tint behind the beat that is sounding.</summary>
    public Color HighlightBackground { get; set; } = Color.FromRgb(0x1E, 0x3A, 0x2A);
    /// <summary>Draw the sounding-beat band at all.</summary>
    public bool HighlightPlayedBeat { get; set; } = true;
    public bool ShowSectionHeadings { get; set; } = true;
    public bool ShowBarNumbers { get; set; } = true;
    public int BarNumberFrequency { get; set; } = 1;
    public double HoverHighlightIntensity { get; set; } = 0.27;
    public double SelectionHighlightIntensity { get; set; } = 0.25;
    public Color SelectionColor { get; set; } = Color.FromRgb(0x4C, 0x9A, 0xFF);
    public Color HoverColor { get; set; } = Color.FromRgb(0x98, 0xA1, 0xAE);

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

    /// <summary>Centres each engraved system in continuous, viewport-reflowing score mode.</summary>
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

    /// <summary>Show a translucent band over the bar that is playing. Off by default.</summary>
    public bool PlayingBarEnabled { get => _barEnabled; set { if (_barEnabled == value) return; _barEnabled = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Colour of the playing-bar band (its opacity is applied on top).</summary>
    public Color PlayingBarColor { get => _barColor; set { if (_barColor == value) return; _barColor = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Opacity of the band, 0..1.</summary>
    public double PlayingBarOpacity { get => _barOpacity; set { if (_barOpacity == value) return; _barOpacity = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }

    /// <summary>Also band the edit cursor's bar while playback is stopped.</summary>
    public bool PlayingBarWhenStopped { get => _barWhenStopped; set { if (_barWhenStopped == value) return; _barWhenStopped = value; _host.AppearanceChanged(ScoreAppearanceChange.Repaint); } }
}
