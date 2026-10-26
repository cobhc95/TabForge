// Owns: the "Save song with audio settings" question's texts and its three options (title, explanation, value, which is recommended).
// Does not own: the window (Avalonia PluginSaveWindow) or the save itself (SaveDialogs maps the value for DocumentSaveFlow).
// Tests: TabForge.Avalonia.Tests.ClipSnapPluginDialogTests.
namespace TabForge.Presenters;

public static class PluginSaveChoicePresenter
{
    /// <summary>.gp with the TabForge data inside (other programs skip it). Default.</summary>
    public const string GpWithData = "gp+data";
    /// <summary>A clean .gp plus "song.tfaudio" beside it: the most compatible.</summary>
    public const string GpPlusDataFile = "gp+file";
    /// <summary>TabForge's own format.</summary>
    public const string TForge = "tforge";

    public const string Title = "Save song with audio settings";
    public const string Headline = "This song uses TabForge audio settings (VST plug-ins, FX chains or mixer groups).";
    public const string CancelButton = "Cancel";

    public static string Detail(string fileName) =>
        $"Other programs do not know these settings: they play {fileName} with their own sounds, and if one saves the file, "
        + "the TabForge settings are removed. How should TabForge save it?";

    public sealed record Option(string Title, string Text, string Value, bool Recommended);

    public static readonly IReadOnlyList<Option> Options =
    [
        new(".gp file with the audio settings inside (recommended)",
            "One .gp file for both: TabForge keeps everything, other programs skip the TabForge part. "
            + "For the best compatibility with other programs, choose the next option.", GpWithData, true),
        new("Clean .gp file + audio data file",
            "The .gp contains nothing TabForge-specific (plain notation only); the audio settings go in a "
            + ".tfaudio file beside it, which TabForge loads automatically when you open the .gp.", GpPlusDataFile, false),
        new("TabForge project (.tforge)", "TabForge's own format: keeps everything. Other programs cannot open it.", TForge, false),
    ];
}
