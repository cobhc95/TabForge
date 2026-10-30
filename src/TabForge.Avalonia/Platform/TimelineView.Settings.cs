using TabForge.Avalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Models;
using TabForge.Platform;
using TabForge.Platform.Theme;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Platform;

/// <summary>
/// The timeline's display settings: the options the Timeline and Appearance settings set (grid, bar numbers, section names, glow, track
/// lines, selected-track shade, playhead style, section bracket and glow strength, master lane look), each redrawing the body on change.
/// Owns: those values and their redraw. Does not own: the settings store (ThemeSettingsBinding writes them), the drawing that reads them
/// (TimelineView.Drawing) or the layout (TimelineView).
/// Tests: TabForge.Avalonia.Tests.TimelineViewTests.
/// </summary>
public sealed partial class TimelineView
{
    private bool _matchSimilarSectionColours = true;

    /// <summary>Whether sections with the same base name show the same colour (display only).</summary>
    public bool MatchSimilarSectionColours
    {
        get => _matchSimilarSectionColours;
        set { if (_matchSimilarSectionColours == value) return; _matchSimilarSectionColours = value; Reload(_song); }
    }

    private bool _hideEmptyGrid = new TimelineSettings().HideEmptyGrid;
    private bool _showBarNumbers = new TimelineSettings().ShowBarNumbers;
    private bool _showSectionNames = new TimelineSettings().ShowSectionNames;
    private bool _barGlow = new TimelineSettings().BarGlow;
    private bool _showTrackLines = new TimelineSettings().ShowTrackLines;
    private bool _shadeSelectedTrack = new TimelineSettings().ShadeSelectedTrack;
    private string _playheadStyle = new TimelineSettings().PlayheadStyle;
    private double _sectionBracketThickness = new AppearanceSettings().SectionBracketThickness;
    private double _sectionGlowIntensity = new FollowSettings().SectionGlowIntensity;
    private MasterLaneStrip.Look _masterLook = MasterLaneStrip.Look.Default;

    /// <summary>Hide the grid in empty bars (the lane stays bare).</summary>
    public bool HideEmptyGrid { get => _hideEmptyGrid; set => Redraw(ref _hideEmptyGrid, value); }

    /// <summary>The bar numbers in the ruler.</summary>
    public bool ShowBarNumbers { get => _showBarNumbers; set => Redraw(ref _showBarNumbers, value); }

    /// <summary>The names inside the section blocks.</summary>
    public bool ShowSectionNames { get => _showSectionNames; set => Redraw(ref _showSectionNames, value); }

    /// <summary>The soft white glow round the bars that have notes.</summary>
    public bool BarGlow { get => _barGlow; set => Redraw(ref _barGlow, value); }

    /// <summary>A line under every track row.</summary>
    public bool ShowTrackLines { get => _showTrackLines; set => Redraw(ref _showTrackLines, value); }

    /// <summary>The grey overlay over the selected track's lane (and, read by the track list, over its row).</summary>
    public bool ShadeSelectedTrack { get => _shadeSelectedTrack; set => Redraw(ref _shadeSelectedTrack, value); }

    /// <summary>The playback position marker: the line, a marker in the played bar's cell, or both (<see cref="PlayheadStyles"/>).</summary>
    public string PlayheadStyle
    {
        get => _playheadStyle;
        set { var style = PlayheadStyles.Normalize(value); if (style == _playheadStyle) return; _playheadStyle = style; InvalidateVisual(); PlayheadLayer.Redraw(); }
    }

    /// <summary>The settings value of the section bracket thickness (1 to 24); the drawn stroke is this divided by <see cref="SectionBracketThicknessDivisor"/>.</summary>
    public double SectionBracketThickness
    {
        get => _sectionBracketThickness;
        set { var clamped = Math.Clamp(double.IsFinite(value) ? value : _sectionBracketThickness, 1, 24); if (clamped == _sectionBracketThickness) return; _sectionBracketThickness = clamped; InvalidateVisual(); }
    }

    /// <summary>How strongly the section holding the playhead glows (0 to 1).</summary>
    public double SectionGlowIntensity
    {
        get => _sectionGlowIntensity;
        set { var clamped = Math.Clamp(double.IsFinite(value) ? value : _sectionGlowIntensity, 0, 1); if (clamped == _sectionGlowIntensity) return; _sectionGlowIntensity = clamped; InvalidateVisual(); }
    }

    /// <summary>How the master lane colours its lines (by track or one colour) and how opaque they are.</summary>
    public MasterLaneStrip.Look MasterLook
    {
        get => _masterLook;
        set { if (_masterLook == value) return; _masterLook = value; InvalidateVisual(); }
    }

    private void Redraw<T>(ref T field, T value) where T : IEquatable<T>
    {
        if (field.Equals(value)) return;
        field = value;
        InvalidateVisual();
    }
}
