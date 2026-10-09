using TabForge.Services;
using TabForge.Views;

namespace TabForge;

/// <summary>Combined paste dialog and its settings rows (docs/COPY_PASTE_DESIGN.md chunk C5).</summary>
public static partial class SelfTest
{
    private static void TestPasteOptionsDialog()
    {
        var one = new PasteOptionsDialog(new[] { PasteQuestion.Drums });
        Check("paste dialog: one question shows only that question", one.ShownQuestions.SequenceEqual(new[] { PasteQuestion.Drums }));
        var two = new PasteOptionsDialog(new[] { PasteQuestion.BarsOntoNotes, PasteQuestion.BeatsOntoNotes });
        Check("paste dialog: only the given questions, in design order",
            two.ShownQuestions.SequenceEqual(new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.BarsOntoNotes }));
        var all = new PasteOptionsDialog(PasteQuestionInfo.All);
        Check("paste dialog: all five questions can be shown", all.ShownQuestions.Count == 5);

        var h1 = one.MeasureContentHeight();
        var h2 = two.MeasureContentHeight();
        var h5 = all.MeasureContentHeight();
        Check("paste dialog: height grows with the number of questions", h1 < h2 && h2 < h5, $"{h1:0} < {h2:0} < {h5:0}");
        Check("paste dialog: a single question is compact", h1 < 260, $"{h1:0}");

        Check("paste dialog: the recommended option is preselected", PasteQuestionInfo.All.All(q => all.ChosenIndex(q) == 0));
        Check("paste dialog: nothing is remembered by default", PasteQuestionInfo.All.All(q => !all.CurrentAnswers().IsRemembered(q)));

        all.Choose(PasteQuestion.Octave, 1, remember: true);
        all.Choose(PasteQuestion.BarsOntoNotes, 2, remember: true);
        all.Choose(PasteQuestion.Drums, 1, remember: false);
        var a = all.CurrentAnswers();
        Check("paste dialog: answers come back per question",
            a.Octave == OctaveAnswer.ShiftOctave && a.BarsOntoNotes == BarsOntoNotesAnswer.InsertAfter && a.Drums == DrumsAnswer.DontPaste
            && a.BeatsOntoNotes == BeatsOntoNotesAnswer.Replace && a.BarSettings == BarSettingsAnswer.CopySettings);
        Check("paste dialog: remember flags come back per question",
            a.RememberOctave && a.RememberBarsOntoNotes && !a.RememberDrums && !a.RememberBeatsOntoNotes && !a.RememberBarSettings);

        one.Choose(PasteQuestion.Drums, 0, remember: true);
        Check("paste dialog: questions that were not asked have no answer",
            one.CurrentAnswers() is { Octave: null, BarsOntoNotes: null, BeatsOntoNotes: null, BarSettings: null, Drums: DrumsAnswer.RhythmOntoOneSound });
        Check("paste dialog: no answers before Paste is pressed", one.Answers is null);
        one.Finish(true);
        Check("paste dialog: Paste confirms and returns the answers", one.Confirmed && one.Answers is { RememberDrums: true });
        two.Finish(false);
        Check("paste dialog: Cancel returns no answers", !two.Confirmed && two.Answers is null);

        var wording = string.Join(" ", PasteQuestionInfo.All.SelectMany(q => PasteQuestionInfo.Options(q).Select(o => o.Label).Append(PasteQuestionInfo.Title(q))));
        Check("paste dialog: wording never names another product",
            !new[] { "guitar pro", "gp5", "reaper", "tuxguitar" }.Any(w => wording.Contains(w, StringComparison.OrdinalIgnoreCase)));
    }

    private static void TestPasteSettingsRows()
    {
        var settings = new AppSettings();
        var catalog = SettingsCatalog.Build(settings);
        // Beats onto notes has no row: pasting onto written beats always inserts; only Paste special still offers Replace.
        var expectedKeys = PasteQuestionInfo.All.Where(q => q != PasteQuestion.BeatsOntoNotes).ToDictionary(q => q, PasteQuestionInfo.SettingKey);
        Check("paste settings: one row per question with a distinct key", expectedKeys.Values.Distinct().Count() == 4
            && expectedKeys.Values.All(k => catalog.Count(d => d.Key == k) == 1));
        foreach (var (q, key) in expectedKeys)
        {
            var row = catalog.First(d => d.Key == key);
            var options = PasteQuestionInfo.Options(q);
            Check($"paste settings: {key} sits in Editing > Copy and paste", row.Category == SettingsCatalog.Editing && row.Group == "Copy and paste");
            Check($"paste settings: {key} offers Ask every time plus each answer",
                row.Choices.Count == options.Count + 1 && row.Choices[0] == "Ask every time" && options.All(o => row.Choices.Contains(o.Label)));
            Check($"paste settings: {key} defaults to Ask every time", Equals(row.Get(), "Ask every time") && PasteQuestionInfo.Get(settings.Editing, q) == "Ask");
            row.Set(options[^1].Label);
            Check($"paste settings: {key} maps to its stored value", PasteQuestionInfo.Get(settings.Editing, q) == options[^1].Id && Equals(row.Get(), options[^1].Label));
            Check($"paste settings: {key} is a remembered answer for the paste commands", PasteQuestionInfo.IdOf(PasteQuestionInfo.Stored(settings.Editing), q) == options[^1].Id);
            row.Set("Ask every time");
            Check($"paste settings: {key} resets to Ask", PasteQuestionInfo.Get(settings.Editing, q) == "Ask" && !PasteQuestionInfo.Stored(settings.Editing).Has(q));
        }
        foreach (var word in new[] { "paste", "copy", "octave", "drums", "bar settings", "insert", "replace" })
            Check($"paste settings: searching '{word}' finds a paste row", catalog.Any(d => expectedKeys.Values.Contains(d.Key) && SettingsCatalog.Matches(d, word)));

        // Remember writes only the ticked, asked questions; unknown stored text falls back to Ask.
        var answers = new PasteAnswers(BeatsOntoNotesAnswer.Insert, true, OctaveAnswer.ShiftOctave, false);
        PasteQuestionInfo.Remember(settings.Editing, answers, new[] { PasteQuestion.BeatsOntoNotes, PasteQuestion.Octave });
        Check("paste settings: Remember stores only the ticked answers",
            settings.Editing.PasteBeatsOntoNotes == "Insert" && settings.Editing.PasteOctave == "Ask");
        Check("paste settings: remembered questions are not asked again",
            PasteQuestionInfo.StillToAsk(settings.Editing, PasteQuestionInfo.All).SequenceEqual(PasteQuestionInfo.All.Where(q => q != PasteQuestion.BeatsOntoNotes)));
        settings.Editing.PasteDrums = "garbage";
        SettingsValidator.Normalize(settings);
        Check("paste settings: an unknown stored value becomes Ask", settings.Editing.PasteDrums == "Ask" && settings.Editing.PasteBeatsOntoNotes == "Insert");
    }
}
