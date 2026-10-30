namespace TabForge.Rendering;

// Owns: the last-used File > Export MIDI dialog options (stored in the app settings).
// Does not own: the range math (RenderSpecBuilder, RenderBarRange), the dialog (Views/MidiExportWindow) or the file encoding (MidiExportService).
// Tests: MidiExportRangeTests.
/// <summary>Last-used File > Export MIDI options. Bounds reuses <see cref="RenderBounds"/> (Song, Bars, CustomBars, Custom)
/// so the dialog maps ranges exactly like Render.</summary>
public sealed class MidiExportSettings
{
    /// <summary>Checked track names (null = not chosen yet: every track). Audio tracks are never written.</summary>
    public List<string>? TrackNames { get; set; }
    /// <summary>A <see cref="RenderBounds"/> value: Song (whole song), Bars (selected bars), CustomBars (bar range) or Custom (time range).</summary>
    public int Bounds { get; set; }
    /// <summary>Bar range: 1-based first / last bar (0 = not chosen yet: bar 1 / the last bar).</summary>
    public int CustomFromBar { get; set; }
    /// <summary>Bar range: 1-based first / last bar (0 = not chosen yet: bar 1 / the last bar).</summary>
    public int CustomToBar { get; set; }
    public double CustomStartSec { get; set; }
    public double CustomEndSec { get; set; }
    /// <summary>True: the export uses each track's articulation map (or a picked preset) instead of today's plain export.</summary>
    public bool UseArticulationMap { get; set; }
    /// <summary>0: techniques without a rule keep their own sound (the default); 1: they are left out.</summary>
    public int UnmappedTechniques { get; set; }
    /// <summary>True: the file also carries TF1 technique markers so TabForge can restore the techniques on import (default off).</summary>
    public bool KeepTechniques { get; set; }
    /// <summary>True: the file carries each song section as a standard SMF marker on the conductor track (default on).</summary>
    public bool WriteSectionMarkers { get; set; } = true;
    /// <summary>The preset picked for a track in the last export (track name to preset name); only for the dialog, never applied to the track.</summary>
    public Dictionary<string, string>? TrackPresetOverrides { get; set; }
}
