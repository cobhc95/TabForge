// Owns: the wording and buttons of the question asked before a copy for older programs (.gp3, .gp4, .gp5) is written: title, headline, intro, advice, the list text and two buttons.
// Does not own: what is lost (Services/LegacyGpPreflight) or the window (Views/GpExportPreflightWindow).
// Tests: TestLegacyGpExport, GpExportPreflightWindowTests.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>The texts of the older-format preflight window. It reuses the buttons and choices of <see cref="GpExportPreflightPresenter"/>: "Save copy" is the compatible choice.</summary>
public static class LegacyGpPreflightPresenter
{
    public const string SaveLabel = "Save copy";
    public const string Advice = "Save copy writes the file with these changes. Your song and its unsaved state are not changed.";

    public static string Title(LegacyGpFormat format) => $"Save {LegacyGpFormats.Extension(format)} copy";

    public static string Headline(string fileName) => $"{fileName} cannot hold everything in this song.";

    public static string Intro(LegacyGpFormat format) =>
        $"A {LegacyGpFormats.Extension(format)} file is for older programs. It keeps the notes and the common marks, but these things are changed or left out:";

    public static string ListName(LegacyGpFormat format) => $"What a {LegacyGpFormats.Extension(format)} file leaves out";

    public static string ListText(GpPreflightReport report) => report.Summary(GpExportPreflightPresenter.ListLineLimit);

    /// <summary>Save copy (Enter) and Cancel (Esc).</summary>
    public static IReadOnlyList<GpExportPreflightButton> Buttons() =>
    [
        new GpExportPreflightButton(SaveLabel, GpExportChoice.ExportCompatible, IsDefault: true, IsCancel: false),
        new GpExportPreflightButton(GpExportPreflightPresenter.CancelLabel, GpExportChoice.Cancel, IsDefault: false, IsCancel: true),
    ];
}
