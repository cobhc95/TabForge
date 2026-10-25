using System;
using TabForge.Avalonia.Views.Docking;
using TabForge.Docking;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;
using TabForge.Visualization;

namespace TabForge.Avalonia.Platform;

// Owns: mapping the Fretboard & Keyboard settings onto the instrument panel: its Look (frets, left-handed, keyboard keys, preview,
//   marker and number sizes, string spacing, board position, note names, scale highlight, colours, follow), the kind override
//   (InstrumentView), the instrument pane's visibility and its side of the score (FretboardAtBottom through
//   DockLayoutTree.WithInstrumentSide). Colour names map to theme-table keys here; every target is written only when its value changes.
// Does not own: the drawing and geometry (InstrumentPanelView, FretboardGeometryRules, KeyboardGeometryRules), the dock rules
//   (DockLayoutTree), the settings file (MainWindowSettings), the palette values (Platform/Theme/Palette).
// Tests: TabForge.Avalonia.Tests.FretboardSettingsBindingTests.
public sealed class FretboardSettingsBinding
{
    /// <summary>The dock pane id of the instrument (fretboard) pane.</summary>
    internal const string InstrumentPaneId = "instrument";

    private AppSettings _settings;
    private DockWorkspaceController? _dock;
    private InstrumentPanelStrip? _strip;
    private Look? _applied;
    private bool _bottomApplied;
    private bool _lockApplied;
    private bool _showApplied;

    public FretboardSettingsBinding(AppSettings settings)
    {
        _settings = settings;
        _bottomApplied = settings.Appearance.FretboardAtBottom;
        _lockApplied = settings.Appearance.LockInstrumentSize;
        _showApplied = settings.Appearance.ShowFretboard;
    }

    /// <summary>The panel's settings-driven values; each is the setting's own type, a Core helper's number, or a theme-table key.</summary>
    public sealed record Look(
        int Frets,
        bool LeftHanded,
        int KeyboardKeys,
        int PreviewHorizon,
        double FretNumberScale,
        int MarkerSizePercent,
        double StringSpacing,
        string InstrumentView,
        bool ShowNoteNames,
        bool FollowFretboard,
        FretboardHorizontalPosition FretboardPosition,
        bool LockInstrumentSize,
        int ScaleRoot,
        string ScalePitches,
        string ScaleStyle,
        string ScaleColourKey,
        double ScaleStrength,
        string MarkerColourKey,
        double MarkerBrightness,
        string WhiteKeyKey)
    {
        /// <summary>The look the settings describe; preview is off when preview notes are off, whatever the horizon.</summary>
        public static Look From(AppSettings settings)
        {
            var editing = settings.Editing;
            var scale = ScaleOf(editing.ScaleHighlight);
            return new Look(
                Frets: editing.FretboardFrets,
                LeftHanded: editing.LeftHanded,
                KeyboardKeys: editing.KeyboardKeys,
                PreviewHorizon: editing.PreviewNotesEnabled ? editing.PreviewHorizon : 0,
                FretNumberScale: FretNumberSizes.Scale(editing.FretNumberSize),
                MarkerSizePercent: editing.FretMarkerSizePercent,
                StringSpacing: FretStringSpacings.Factor(editing.FretStringSpacing),
                InstrumentView: editing.InstrumentView,
                ShowNoteNames: editing.ShowNoteNames,
                FollowFretboard: settings.Follow.FollowFretboard,
                FretboardPosition: HorizontalPosition(settings.Appearance.FretboardPosition),
                LockInstrumentSize: settings.Appearance.LockInstrumentSize,
                ScaleRoot: scale.Root,
                ScalePitches: scale.Pitches,
                ScaleStyle: editing.ScaleHighlightStyle,
                ScaleColourKey: ColourKey(editing.ScaleHighlightColour),
                ScaleStrength: ScaleHighlightStyles.StrengthFactor(editing.ScaleHighlightStrength),
                MarkerColourKey: ColourKey(editing.FretMarkerColour),
                MarkerBrightness: FretMarkerLevels.Level(editing.FretMarkerBrightness),
                WhiteKeyKey: WhiteKeysPaletteKey(editing.KeyboardKeyColours));
        }
    }

    /// <summary>Where the board sits beside the legend column: the Left, Centre or Right setting; anything else is Centre.</summary>
    public static FretboardHorizontalPosition HorizontalPosition(string? position) => position switch
    {
        "Left" => FretboardHorizontalPosition.Left,
        "Right" => FretboardHorizontalPosition.Right,
        _ => FretboardHorizontalPosition.Centre,
    };

    /// <summary>The theme-table key of a colour name from the Scale highlight, Fret marker colour settings; "Default" is the lit-note green.</summary>
    public static string ColourKey(string? name) => name switch
    {
        "Accent" => "Accent",   // the applied accent, live
        "Blue" => "InstrumentAccent",
        "Amber" => "InstrumentNext",
        "Purple" => "InstrumentPurple",
        "Red" => "InstrumentRed",
        "Teal" => "InstrumentTeal",
        "Grey" => "InstrumentGrey",
        "White" => "InstrumentWhite",
        "Silver" => "InstrumentSilver",
        _ => "InstrumentCurrent",
    };

    /// <summary>The theme-table key of the white keys under the Keyboard key colours setting.</summary>
    public static string WhiteKeysPaletteKey(string? style) => style switch
    {
        KeyboardKeyStyles.Grey => "KeyboardGreyKey",
        KeyboardKeyStyles.White => "KeyboardWhiteKeyBright",
        _ => "KeyboardWhiteKey",
    };

    /// <summary>The root's pitch-class index and the 12 pitch classes of a "Root Scale" highlight ("Off" or empty gives none).</summary>
    private static (int Root, string Pitches) ScaleOf(string? highlight)
    {
        var none = (-1, "");
        if (string.IsNullOrWhiteSpace(highlight) || highlight == ScaleFinderPresenter.OffHighlight) return none;
        var parts = highlight.Split(' ', 2);
        if (parts.Length < 2) return none;
        var pitches = new char[12];
        Array.Fill(pitches, '0');
        foreach (var note in MusicTheoryService.ScaleNotes(parts[0], parts[1]))
        {
            var index = Array.IndexOf(MusicTheoryService.NoteNames, note);
            if (index >= 0) pitches[index] = '1';
        }
        return (Array.IndexOf(MusicTheoryService.NoteNames, parts[0]), new string(pitches));
    }

    /// <summary>The panel kind for a track under the InstrumentView setting: a named view wins, "Match the instrument" follows the track.</summary>
    public static InstrumentPanelKind KindFor(TrackModel track, string instrumentView) => instrumentView switch
    {
        InstrumentViews.Fretboard => InstrumentPanelKind.Fretboard,
        InstrumentViews.Keyboard => InstrumentPanelKind.Keyboard,
        InstrumentViews.Drums => InstrumentPanelKind.Drums,
        _ => InstrumentPanelStrip.KindOf(track),
    };

    /// <summary>The dock this binding moves the instrument pane in; set once the window's dock exists.</summary>
    public void BindDock(DockWorkspaceController dock) => _dock = dock;

    /// <summary>Makes <paramref name="strip"/> the panel the settings reach (the dock rebuilds it on change) and gives it the current look.</summary>
    public InstrumentPanelStrip Attach(InstrumentPanelStrip strip)
    {
        _strip = strip;
        _applied = null;
        Apply(_settings);
        return strip;
    }

    /// <summary>Applies the settings to the panel and the dock; called on every settings change.</summary>
    public void Apply(AppSettings settings)
    {
        _settings = settings;
        var look = Look.From(settings);
        if (_strip is not null && look != _applied)
        {
            _applied = look;
            _strip.Panel.Look = look;
            _strip.Refresh();
        }
        if (_dock is null) return;
        var locked = settings.Appearance.LockInstrumentSize;
        if (locked != _lockApplied)
        {
            _lockApplied = locked;
            _dock.SetNaturalHeightLock(InstrumentPaneId, locked);
        }
        // Only a change of the setting moves the pane: a pane the user closed stays closed while the setting still says shown.
        var shown = settings.Appearance.ShowFretboard;
        if (_showApplied != shown && _dock.IsPanelVisible(InstrumentPaneId) != shown)
            _dock.SetPanelVisible(InstrumentPaneId, shown);
        _showApplied = shown;
        var bottom = settings.Appearance.FretboardAtBottom;
        if (bottom != _bottomApplied)
        {
            _bottomApplied = bottom;
            _dock.ApplyLayout(DockLayoutTree.WithInstrumentSide(_dock.SnapshotLayout(), bottom));
        }
    }
}
