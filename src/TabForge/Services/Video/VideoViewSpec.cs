using TabForge.Models;

namespace TabForge.Services.Video;

public enum VideoLayout { Focus, ScoreOnly, Band, ScoreAndBand }

// What a video frame shows: the layout, the track(s), the theme and the size.
// Owns: the choices only.
// Does not own: drawing (VideoFrameSource) or the export (VideoExportFlow).
// Tests: TestVideoExport.
public sealed class VideoViewSpec
{
    public VideoLayout Layout { get; init; } = VideoLayout.Focus;
    /// <summary>Song track indexes. The score and the instrument show the first; the Band view shows all of them.</summary>
    public IReadOnlyList<int> Tracks { get; init; } = new[] { 0 };
    public bool Dark { get; init; } = true;
    /// <summary>The fretboard, keyboard or drums (the Band view's instruments in the Band layouts).</summary>
    public bool ShowInstrument { get; init; } = true;
    public int Width { get; init; } = 1920;
    public int Height { get; init; } = 1080;
    public NotationMode Notation { get; init; } = NotationMode.TabAndStaff;
}

// The video export window's remembered choices (File > Export > Video).
// Owns: the saved values.
// Does not own: the window that shows them or the export.
// Tests: TestVideoExport.
public sealed class VideoSettings
{
    public bool Remember { get; set; } = true;
    public int Range { get; set; }                 // 0 whole song, 1 selection, 2 bars, 3 section
    public int FromBar { get; set; } = 1;
    public int ToBar { get; set; } = 1;
    public int Section { get; set; }
    public int Layout { get; set; }                // VideoLayout
    public List<string>? TrackNames { get; set; }
    public bool Dark { get; set; } = true;
    public bool ShowInstrument { get; set; } = true;
    public int Height { get; set; } = 1080;        // 1080 or 2160
    public int Fps { get; set; } = 30;
    public string Directory { get; set; } = "";
}
