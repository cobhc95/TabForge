using System.IO;
using TabForge.Audio.Contracts;
using TabForge.Models;
using TabForge.Services.Video;

namespace TabForge.Presenters;

/// <summary>What the video export window's controls hold, read back when a choice is remembered or the video is exported.</summary>
public sealed record VideoExportChoices(
    bool Remember, int Range, string FromText, string ToText, int Section, int Layout, bool Dark, bool ShowInstrument, int ResolutionIndex,
    string? FpsChoice, IReadOnlyList<string> TrackNames, IReadOnlyList<bool> TrackChecks, string Encoder = VideoEncoderChoices.Auto);

/// <summary>
/// The video export window's option lists, defaults and rules, its texts, and what its Export button reports.
/// Owns: the choice lists (range, layout, theme, resolution, frame rate) and the index of each choice, the mapping between the remembered
///     settings and the controls, the track checks, which controls each range enables, the range problems, the save dialog's name and folder
///     rules, and the status texts.
/// Does not own: the window (Views/VideoExportWindow.cs), the bar-to-time mapping (RenderSpecBuilder), the export (VideoExportFlow) or the encoder.
/// Tests: no dedicated test class.
/// </summary>
public static class VideoExportPresenter
{
    /// <summary>The export window title.</summary>
    public const string WindowTitle = "Export video (MP4)";
    /// <summary>The range choices: entire song, selection, bars, section.</summary>
    public static readonly IReadOnlyList<string> RangeChoices = new[] { "Entire song", "Selection", "Bars", "Section" };
    /// <summary>The layout choices: focus track, score only, band, score and band.</summary>
    public static readonly IReadOnlyList<string> LayoutChoices = new[]
    {
        "Focus on one track (score and instrument)", "Score only", "Band view", "Score and Band view",
    };
    /// <summary>The theme choices: dark, light.</summary>
    public static readonly IReadOnlyList<string> ThemeChoices = new[] { "Dark", "Light" };
    /// <summary>The resolution choices: 1080p, 4K.</summary>
    public static readonly IReadOnlyList<string> ResolutionChoices = new[] { "1080p (1920 x 1080)", "4K (3840 x 2160)" };
    /// <summary>The frame rate choices: 30, 60, 120.</summary>
    public static readonly IReadOnlyList<string> FpsChoices = new[] { "30", "60", "120" };
    /// <summary>The encoder choices: Auto (hardware once it passed the self-test, as live recording), Hardware, Software.</summary>
    public static readonly IReadOnlyList<string> EncoderChoices = new[] { VideoEncoderChoices.Auto, VideoEncoderChoices.Hardware, VideoEncoderChoices.Software };
    /// <summary>Tip of the encoder choice.</summary>
    public const string EncoderToolTip = "Auto uses the hardware encoder once it passed a quick self-test; a hardware encoder that stalls is replaced by the software encoder";

    /// <summary>Range indexes: whole song, selection, bars, section.</summary>
    public const int RangeWholeSong = 0, RangeSelection = 1, RangeBars = 2, RangeSection = 3;
    /// <summary>Resolution indexes: HD, UHD.</summary>
    public const int ResolutionHd = 0, ResolutionUhd = 1;
    private const int HdWidth = 1920, HdHeight = 1080, UhdWidth = 3840, UhdHeight = 2160;
    private const int ThemeDarkIndex = 0, ThemeLightIndex = 1;
    private const int LayoutFocusIndex = (int)VideoLayout.Focus, LayoutScoreOnlyIndex = (int)VideoLayout.ScoreOnly;
    private const int DefaultFps = 30;
    private const double MinimumRangeMs = 100;

    /// <summary>Tip: only the first checked track is shown in focus layouts.</summary>
    public const string FocusTrackToolTip = "Only the first checked track is shown";
    /// <summary>Range problem: select bars first.</summary>
    public const string SelectSomeBarsProblem = "Select some bars first.";
    /// <summary>Range problem: the song has no section.</summary>
    public const string NoSectionProblem = "The song has no section to export.";
    /// <summary>Range problem: the chosen range is empty.</summary>
    public const string EmptyRangeProblem = "The chosen range is empty.";

    /// <summary>The save dialog title.</summary>
    public const string SaveDialogTitle = "Export video";
    /// <summary>The save dialog filter for MP4 files.</summary>
    public const string SaveDialogFilter = "MP4 video (*.mp4)|*.mp4";
    /// <summary>The default export file extension.</summary>
    public const string DefaultExtension = ".mp4";

    /// <summary>Export button text.</summary>
    public const string ExportButton = "Export";
    /// <summary>Close button text.</summary>
    public const string CloseButton = "Close";
    /// <summary>Cancel-export button text.</summary>
    public const string CancelExportButton = "Cancel export";
    /// <summary>Status text while the audio renders.</summary>
    public const string RenderingAudioText = "Rendering the audio…";
    /// <summary>Status text when the export is cancelled.</summary>
    public const string CancelledText = "Export cancelled.";

    /// <summary>The missing plug-ins question title.</summary>
    public const string MissingPluginsTitle = "Plug-ins not loaded";
    /// <summary>Tip for exporting without the missing plug-ins.</summary>
    public const string ExportWithoutPluginsToolTip = "Export without the missing plug-ins";
    /// <summary>Tip for cancelling the export.</summary>
    public const string CancelExportToolTip = "Cancel the export";

    /// <summary>The remembered range, except that a remembered Selection opens as the whole song when no bars are selected.</summary>
    public static int RangeOnOpen(int savedRange, bool hasSelection) =>
        !hasSelection && savedRange == RangeSelection ? RangeWholeSong : Math.Clamp(savedRange, 0, RangeChoices.Count - 1);

    /// <summary>The remembered layout, clamped to the layout choices.</summary>
    public static int LayoutOnOpen(int savedLayout) => Math.Clamp(savedLayout, 0, LayoutChoices.Count - 1);

    /// <summary>The remembered section, or -1 (no choice) when the song has no sections.</summary>
    public static int SectionOnOpen(int savedSection, int sectionCount) => sectionCount > 0 ? Math.Clamp(savedSection, 0, sectionCount - 1) : -1;

    /// <summary>A bar box's remembered value, kept within 1 to the song's bar count.</summary>
    public static int ClampBar(int bar, int barCount) => Math.Clamp(bar, 1, Math.Max(1, barCount));

    /// <summary>The theme index for a dark or light choice.</summary>
    public static int ThemeIndex(bool dark) => dark ? ThemeDarkIndex : ThemeLightIndex;

    /// <summary>The resolution index for a render height (UHD at 2160 or above).</summary>
    public static int ResolutionIndex(int height) => height >= UhdHeight ? ResolutionUhd : ResolutionHd;

    /// <summary>The render height for a resolution choice (1080p or 4K).</summary>
    public static int ResolutionHeight(int resolutionIndex) => resolutionIndex == ResolutionUhd ? UhdHeight : HdHeight;

    /// <summary>The render width for a resolution choice (1080p or 4K).</summary>
    public static int ResolutionWidth(int resolutionIndex) => resolutionIndex == ResolutionUhd ? UhdWidth : HdWidth;

    /// <summary>The frame rate choice for a remembered frame rate; anything but 60 or 120 opens as 30.</summary>
    public static string FpsChoice(int fps) => fps is 60 or 120 ? fps.ToString() : DefaultFps.ToString();

    /// <summary>The encoder choice for a remembered encoder; anything unknown opens as Auto.</summary>
    public static string EncoderChoice(string? encoder) => EncoderChoices.FirstOrDefault(c => c == encoder) ?? VideoEncoderChoices.Auto;

    /// <summary>The frame rate a choice means; a missing or non-number choice means 30.</summary>
    public static int FpsFrom(string? choice) => int.TryParse(choice, out var fps) ? fps : DefaultFps;

    /// <summary>
    /// Which tracks start checked: the remembered names, else the selected tracks, else the first track. The first track is checked when
    /// none of them is found, so a song with tracks always exports at least one.
    /// </summary>
    public static bool[] InitialTrackChecks(IReadOnlyList<string> trackNames, IReadOnlyList<string>? rememberedNames, IReadOnlyList<string> selectedNames)
    {
        var names = new HashSet<string>(rememberedNames ?? (selectedNames.Count > 0 ? selectedNames : trackNames.Take(1).ToList()));
        var checks = trackNames.Select(names.Contains).ToArray();
        if (checks.Length > 0 && !checks.Any(c => c)) checks[0] = true;
        return checks;
    }

    /// <summary>The checked track indexes in song order; the first track when none is checked.</summary>
    public static IReadOnlyList<int> TrackIndexes(IReadOnlyList<bool> checks)
    {
        var indexes = checks.Select((isChecked, index) => (isChecked, index)).Where(x => x.isChecked).Select(x => x.index).ToList();
        if (indexes.Count == 0) indexes.Add(0);
        return indexes;
    }

    /// <summary>The checked track names in song order; a track with no check in the list counts as unchecked.</summary>
    public static IReadOnlyList<string> CheckedTrackNames(IReadOnlyList<string> trackNames, IReadOnlyList<bool> checks) =>
        trackNames.Where((_, index) => index < checks.Count && checks[index]).ToList();

    /// <summary>True when the bar boxes apply (the Bars range).</summary>
    public static bool BarBoxesEnabled(int range) => range == RangeBars;

    /// <summary>True when the section list applies (the Section range with sections present).</summary>
    public static bool SectionListEnabled(int range, int sectionCount) => range == RangeSection && sectionCount > 0;

    /// <summary>The track tool tip: only the focus layouts (Focus on one track, Score only) show just the first checked track.</summary>
    public static string? TrackToolTip(int layout) => layout is LayoutFocusIndex or LayoutScoreOnlyIndex ? FocusTrackToolTip : null;

    /// <summary>Why the chosen range cannot be exported, or null. <paramref name="barProblem"/> is the bar boxes' problem (Bars only).</summary>
    public static string? RangeProblem(int range, bool hasSelection, string? barProblem, bool hasSection) => range switch
    {
        RangeSelection when !hasSelection => SelectSomeBarsProblem,
        RangeBars => barProblem,
        RangeSection when !hasSection => NoSectionProblem,
        _ => null,
    };

    /// <summary>The range is too short to export when it is under a tenth of a second.</summary>
    public static string? EmptyRangeProblemFor(double startMs, double endMs) => endMs - startMs < MinimumRangeMs ? EmptyRangeProblem : null;

    /// <summary>The save dialog's suggested name: the project title made safe for a file name.</summary>
    public static string SuggestedFileName(string projectTitle) => SafeFileNames.SafeFileName(projectTitle, "Untitled");

    /// <summary>The save dialog opens in the remembered folder when it still exists, else in the Videos folder.</summary>
    public static string InitialDirectory(string rememberedDirectory, bool rememberedExists, string videosFolder) =>
        rememberedExists ? rememberedDirectory : videosFolder;

    /// <summary>The folder of a chosen export path (empty when none).</summary>
    public static string DirectoryOf(string chosenPath) => Path.GetDirectoryName(chosenPath) ?? "";

    /// <summary>The Plug-ins not loaded question's message: the missing plug-ins as a bulleted list.</summary>
    public static string MissingPluginsMessage(IEnumerable<string> missing) =>
        "Some plug-ins are not loaded, so the audio would sound different from the song:\n\n" +
        string.Join("\n", missing.Select(name => "• " + name)) + "\n\nExport anyway without them?";

    /// <summary>Progress text with the overall fraction.</summary>
    public static string ProgressText(string text, double fraction) => $"{text} ({fraction:P0})";

    /// <summary>Completion text with the frame count and timings.</summary>
    public static string DoneText(int frames, double seconds, double elapsedSeconds) =>
        $"Done: {frames} frames, {seconds:0.0} s of video in {elapsedSeconds:0.0} s.";

    /// <summary>A failed export says it was cancelled when the render was cancelled, else it says why it failed.</summary>
    public static string FailureText(bool cancelled, string message) => cancelled ? CancelledText : "Export failed: " + message;

    /// <summary>Writes the choices into the remembered settings. Returns false, and changes nothing, when they are not remembered.</summary>
    public static bool Remember(VideoSettings settings, VideoExportChoices choices)
    {
        settings.Remember = choices.Remember;
        if (!choices.Remember) return false;
        settings.Range = choices.Range; settings.Layout = choices.Layout; settings.Dark = choices.Dark; settings.ShowInstrument = choices.ShowInstrument;
        settings.Height = ResolutionHeight(choices.ResolutionIndex);
        settings.Fps = FpsFrom(choices.FpsChoice);
        settings.Encoder = EncoderChoice(choices.Encoder);
        if (int.TryParse(choices.FromText, out var from)) settings.FromBar = from;
        if (int.TryParse(choices.ToText, out var to)) settings.ToBar = to;
        settings.Section = Math.Max(0, choices.Section);
        settings.TrackNames = CheckedTrackNames(choices.TrackNames, choices.TrackChecks).ToList();
        return true;
    }

    /// <summary>The video to render: the layout, the checked tracks, the theme, the instrument and the resolution (1080p or 4K).</summary>
    public static VideoViewSpec ViewSpec(VideoExportChoices choices, NotationMode notation)
    {
        return new VideoViewSpec
        {
            Layout = (VideoLayout)choices.Layout, Tracks = TrackIndexes(choices.TrackChecks), Dark = choices.Dark, ShowInstrument = choices.ShowInstrument,
            Width = ResolutionWidth(choices.ResolutionIndex), Height = ResolutionHeight(choices.ResolutionIndex), Notation = notation,
        };
    }
}
