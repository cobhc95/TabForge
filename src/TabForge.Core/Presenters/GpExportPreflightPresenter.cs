// Owns: the export preflight window's wording and buttons as plain data: the title, headline, intro, advice (for a Save or an Export),
//       the list text and its automation name, and the three buttons with their labels, the choice each gives, and the default (Enter) and Esc roles.
// Does not own: what is lost and how the files are written (Services/GpExportPreflight), or the window and its automation ids (Views/GpExportPreflightDialog).
// Tests: none dedicated(through the window), TestGpPreflightWarnings.
using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>One button of the preflight window: its label, the choice it gives, and whether it is the Enter (default) or the Esc (cancel) button.</summary>
public sealed record GpExportPreflightButton(string Label, GpExportChoice Choice, bool IsDefault, bool IsCancel);

/// <summary>The export preflight window's texts and buttons. Static: the window's state is in the view.</summary>
public static class GpExportPreflightPresenter
{
    /// <summary>The most loss lines the list shows before "and N more".</summary>
    public const int ListLineLimit = 12;

    public const string ListName = "What a compatible .gp file leaves out";
    public const string Intro = "A compatible .gp file keeps the notes and the common marks, but these things are changed or left out:";
    public const string KeepLabel = "Keep a full TabForge copy";
    public const string CancelLabel = "Cancel";

    private const string SaveAdvice =
        "Keep a full TabForge copy writes a .tforge project with everything first, and your song then follows that copy. "
        + "Save compatible .gp writes the .gp plus its audio-settings file (plug-ins, FX chains, mixer groups) and marks the song saved; the notation details listed above are not kept.";
    private const string ExportAdvice =
        "Keep a full TabForge copy also writes a .tforge project with everything beside the file. Your song and its unsaved state are not changed either way.";

    public static string Title(GpExportKind kind) => kind == GpExportKind.Save ? "Save as .gp file" : "Export .gp file";

    public static string Headline(string fileName) => $"{fileName} cannot hold everything in this song.";

    public static string Advice(GpExportKind kind) => kind == GpExportKind.Save ? SaveAdvice : ExportAdvice;

    public static string ListText(GpPreflightReport report) => report.Summary(ListLineLimit);

    /// <summary>The three buttons in order: Keep (Enter, the safe choice that loses nothing), the compatible choice, then Cancel (Esc).</summary>
    public static IReadOnlyList<GpExportPreflightButton> Buttons(GpExportKind kind) =>
    [
        new GpExportPreflightButton(KeepLabel, GpExportChoice.KeepNativeCopy, IsDefault: true, IsCancel: false),
        new GpExportPreflightButton(kind == GpExportKind.Save ? "Save compatible .gp" : "Export compatible copy", GpExportChoice.ExportCompatible, IsDefault: false, IsCancel: false),
        new GpExportPreflightButton(CancelLabel, GpExportChoice.Cancel, IsDefault: false, IsCancel: true),
    ];
}
