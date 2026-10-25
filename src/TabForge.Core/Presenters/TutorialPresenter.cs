using TabForge.Services;

namespace TabForge.Presenters;

/// <summary>One row of the tutorial's contents list: a chapter (Level 0), or a heading inside the open chapter (Level 1 and deeper).</summary>
public sealed record TutorialContentsRow(string Text, TutorialChapter Chapter, string? HeadingId, int Level);

/// <summary>Which of the reader's Back, Forward, Previous and Next buttons are enabled.</summary>
public sealed record TutorialNavButtons(bool Back, bool Forward, bool Previous, bool Next);

/// <summary>What the window shows for its library: the empty message or the reader, and whether search and export are on.</summary>
public sealed record TutorialLibraryState(bool EmptyMessageVisible, bool ReaderVisible, bool SearchEnabled, bool ExportEnabled);

/// <summary>The search area's panels. <see cref="NoResultsText"/> is null when the no-hits text does not change (a blank search).</summary>
public sealed record TutorialSearchPanels(bool HintVisible, bool ClearVisible, bool ContentsVisible, bool ResultsVisible, bool NoResultsVisible, string? NoResultsText);

/// <summary>
/// The Help > Tutorial window's rules without UI types: the guide choice and its title, the contents rows (the chapters in order and the open chapter's headings),
/// the Back, Forward, Previous and Next enabled states, the empty-library state, the search panels, and the PDF export's progress and result texts.
/// Owns: the texts the window shows for these rules, and what each rule decides. The export step names are the ones TutorialPdfExporter reports.
/// Does not own: the reader, the search index and the window (Views/TutorialWindow.xaml.cs), the content (TutorialLibrary), the PDF writer (Views/TutorialPdfExporter.cs),
///     the saved guide choice (AppSettingsStore) and the export run (the window's RunExportAsync).
/// Tests: no dedicated test class.
/// </summary>
public static class TutorialPresenter
{
    /// <summary>The saved guide choice for the Detailed Guide; Basic is any other value.</summary>
    private const string DetailedSavedValue = "detailed";

    /// <summary>The last export step; the exporter reports it when the PDF is finished.</summary>
    public const string DoneStep = "Done";

    /// <summary>The export step during which the page layout runs with no measurable progress.</summary>
    public const string LayingOutStep = "Laying out pages";

    public const string PreparingText = "Preparing the PDF…";
    public const string CancellingText = "Cancelling…";
    public const string CancelledText = "PDF export cancelled.";

    /// <summary>The name of a guide as the window's title and export dialog use it.</summary>
    private static string GuideName(TutorialGuide guide) => guide == TutorialGuide.Detailed ? "Detailed Guide" : "Basic Guide";

    /// <summary>The window title for a guide.</summary>
    public static string GuideTitle(TutorialGuide guide) => "TabForge " + GuideName(guide);

    /// <summary>The title of the export dialog for a guide.</summary>
    public static string ExportDialogTitle(TutorialGuide guide) => $"Export the {GuideName(guide)} as a PDF";

    /// <summary>True when the Detailed Guide has chapters, so its choice is enabled.</summary>
    public static bool DetailedAvailable(TutorialLibrary detailed) => detailed.Chapters.Count > 0;

    /// <summary>The guide a window opens on: the requested one, else the saved choice. Detailed falls back to Basic when it is not installed.</summary>
    public static TutorialGuide OpeningGuide(TutorialGuide? requested, string? savedValue, bool detailedAvailable)
    {
        var wanted = requested ?? (string.Equals(savedValue, DetailedSavedValue, StringComparison.OrdinalIgnoreCase) ? TutorialGuide.Detailed : TutorialGuide.Basic);
        return SwitchedGuide(wanted, detailedAvailable);
    }

    /// <summary>The guide a switch lands on: the Detailed Guide only when it is installed.</summary>
    public static TutorialGuide SwitchedGuide(TutorialGuide requested, bool detailedAvailable) =>
        requested == TutorialGuide.Detailed && !detailedAvailable ? TutorialGuide.Basic : requested;

    /// <summary>The value the saved guide choice stores for a guide.</summary>
    public static string SavedGuideValue(TutorialGuide guide) => guide == TutorialGuide.Detailed ? DetailedSavedValue : "basic";

    /// <summary>The contents rows in order: each chapter, and the headings of the open chapter right after it.</summary>
    public static IReadOnlyList<TutorialContentsRow> ContentsRows(IReadOnlyList<TutorialChapter> chapters, TutorialChapter? open)
    {
        var rows = new List<TutorialContentsRow>();
        foreach (var chapter in chapters)
        {
            rows.Add(new TutorialContentsRow(chapter.DisplayTitle, chapter, null, 0));
            if (!ReferenceEquals(chapter, open)) continue;
            foreach (var heading in chapter.SubHeadings) rows.Add(new TutorialContentsRow(heading.Text, chapter, heading.Id, heading.Level - 1));
        }
        return rows;
    }

    /// <summary>The name a screen reader gives a contents row: the chapter's label and title, or the section's text.</summary>
    public static string ContentsAutomationName(TutorialChapter chapter, string text, int level) =>
        level == 0 ? $"{chapter.Label}: {chapter.Title}" : $"Section: {text}";

    /// <summary>Which of Back, Forward, Previous and Next are enabled, from the history position and the open chapter's position in the library.</summary>
    public static TutorialNavButtons NavButtons(IReadOnlyList<TutorialChapter> chapters, TutorialChapter? current, int historyIndex, int historyCount)
    {
        var chapterIndex = current is null ? -1 : chapters.ToList().IndexOf(current);
        return new TutorialNavButtons(
            Back: historyIndex > 0,
            Forward: historyIndex >= 0 && historyIndex < historyCount - 1,
            Previous: chapterIndex > 0,
            Next: chapterIndex >= 0 && chapterIndex < chapters.Count - 1);
    }

    /// <summary>The empty message when there are no chapters; otherwise the reader, search, and export (not while an export runs).</summary>
    public static TutorialLibraryState LibraryState(int chapterCount, bool exporting)
    {
        var any = chapterCount > 0;
        return new TutorialLibraryState(EmptyMessageVisible: !any, ReaderVisible: any, SearchEnabled: any, ExportEnabled: any && !exporting);
    }

    /// <summary>The search text with its ends trimmed; a blank text shows the contents.</summary>
    public static string SearchQuery(string text) => text.Trim();

    /// <summary>The search area's panels for the search box text and the number of hits for its query.</summary>
    public static TutorialSearchPanels SearchPanels(string text, int resultCount)
    {
        var query = SearchQuery(text);
        var hasText = text.Length > 0;
        if (query.Length == 0)
            return new TutorialSearchPanels(HintVisible: !hasText, ClearVisible: hasText, ContentsVisible: true, ResultsVisible: false, NoResultsVisible: false, NoResultsText: null);
        var hits = resultCount > 0;
        return new TutorialSearchPanels(HintVisible: false, ClearVisible: true, ContentsVisible: false, ResultsVisible: hits, NoResultsVisible: !hits,
            NoResultsText: $"Nothing matches “{query}”.");
    }

    /// <summary>The progress bar's maximum: one step per chapter, plus the finishing step.</summary>
    public static int ExportProgressMaximum(int chapterCount) => chapterCount + 1;

    /// <summary>True when the export step runs with no measurable progress.</summary>
    public static bool ExportProgressIsIndeterminate(string step) => step == LayingOutStep;

    /// <summary>The status line for one export step report.</summary>
    public static string ExportProgressStatus(int done, int total, string step) => step switch
    {
        DoneStep => "Finishing…",
        LayingOutStep => "Laying out pages…",
        _ => $"Writing chapter {done + 1} of {total}: {step}",
    };

    /// <summary>The status line after a PDF is saved.</summary>
    public static string ExportSavedText(string fileName, int pages) => $"Saved {fileName} ({pages} pages).  ";

    /// <summary>The status line when the PDF could not be written.</summary>
    public static string ExportFailedText(string reason) => "The PDF could not be written: " + reason;
}
