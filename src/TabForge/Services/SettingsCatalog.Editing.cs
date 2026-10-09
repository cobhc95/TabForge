using TabForge.Documents;
using TabForge.Models;

namespace TabForge.Services;

// Owns: the Editing part of the settings catalogue (rows in Preferences order).
// Does not own: page layout (SettingsCatalog.Layout) or the stored values (AppSettings).
// Tests: TestPreferencesCatalog, TestSettingsFileSplitSnapshots.
public static partial class SettingsCatalog
{
    private static List<SettingDescriptor> EditingRows(AppSettings s)
    {
        var a = s.Appearance!;
        var ed = s.Editing!;
        var timeline = s.Timeline!;
        return new List<SettingDescriptor>
        {
            // Editing
            Choice(Editing, "Entry", "editing.duration", "Default note value", v => ed.DefaultDuration = int.Parse(v), () => ed.DefaultDuration.ToString(),
                new[] { "1", "2", "4", "8", "16", "32", "64" }, "Duration used for newly entered notes.", "whole half quarter eighth sixteenth thirty-second value"),
            Bool(Editing, "Entry", "editing.advance", "Advance after entering a note", v => ed.AutoAdvance = v, () => ed.AutoAdvance,
                "Move the caret forward by the entered note duration.", "auto advance caret move"),
            Bool(Editing, "Entry", "editing.reverseplusminus", "Reverse + / - duration keys", v => ed.ReversePlusMinusDuration = v, () => ed.ReversePlusMinusDuration,
                "Off (default): + makes the note shorter (8th to 16th), - makes it longer. On: the opposite.", "plus minus duration shorter longer reverse gp5"),
            Bool(Editing, "Entry", "editing.preventoverflow", "Prevent rhythms that overfill a bar", v => ed.PreventBarOverflow = v, () => ed.PreventBarOverflow,
                "Off (default): notes can be made longer freely and a bar that no longer adds up turns red. On: such changes are refused.", "red bar overfill duration longer block prevent"),
            Bool(Editing, "Entry", "editing.fillrests", "Fill incomplete bars with rests", v => ed.FillBarsWithRests = v, () => ed.FillBarsWithRests,
                "On (default): a bar you edit always adds up. Empty space becomes rests, a deleted beat becomes a rest of the same length, and a typed note takes its place inside a rest. Off: bars may stay incomplete and show red.", "rest fill complete bar incomplete red empty gap"),
            Choice(Editing, "Entry", "editing.deleteleaves", "When deleting notes, leave", v => ed.MergeRestsOnDelete = v == "Merged rests", () => ed.MergeRestsOnDelete ? "Merged rests" : "A rest of the same length",
                new[] { "A rest of the same length", "Merged rests" }, "Used with \"Fill incomplete bars with rests\". A rest of the same length keeps the deleted note's value. Merged rests join the deleted notes and the rests beside them into the fewest rests that fill the bar (a cleared bar becomes one whole-bar rest).", "delete notes rest merge whole bar clear"),
            Bool(Editing, "Safety", "editing.confirmdelete", "Confirm before deleting a bar", v => ed.ConfirmDeleteBar = v, () => ed.ConfirmDeleteBar,
                "Ask before deleting a bar and shifting later content.", "confirm delete bar prompt", hotkey: "Bar.Delete"),
            Choice(Editing, "Safety", "editing.bardelete", "Ask what Delete does on bars", v => ed.BarRangeDelete = BarRangePromptText.FromChoice(v), () => BarRangePromptText.ToChoice(ed.BarRangeDelete),
                BarRangePromptText.Choices, "Delete on bars selected on the timeline opens a prompt (clear, remove and close the gap, or insert a gap). Pick an answer here to make Delete do it directly; \"Remember my answer\" in the prompt sets this too.", "delete bars timeline prompt ask clear remove gap remember"),
            Choice(Editing, "Converting tracks", "editing.convertmidi", "When converting MIDI clips to an instrument track", v => ed.ConvertMidiClips = v, () => ed.ConvertMidiClips is "Write as notation" or "Keep as MIDI" ? ed.ConvertMidiClips : "Ask",
                new[] { "Ask", "Write as notation", "Keep as MIDI" }, "Convert to instrument track on an audio track that has MIDI clips: ask each time, write the notes into the bars, or keep the clips as MIDI on a second lane. \"Remember my choice\" in the prompt sets this too.", "convert audio track instrument midi clips notation remember"),
            Bool(Editing, "Clips", "editing.trimemptybars", "Remove empty bars at the end when clips shrink", v => ed.TrimEmptyBarsAtEnd = v, () => ed.TrimEmptyBarsAtEnd,
                "After a clip is deleted, cropped or moved away, the song drops the empty bars at its end, down to the last bar that holds notation, an audio clip or a MIDI clip. Bars with notation, sections or markers, mix-table points or repeats are always kept.", "clip delete crop shrink empty bars end song length trim remove"),
            Int(Editing, "Navigation", "editing.scorewheel", "Score wheel scroll distance", v => ed.ScoreWheelScrollPixels = v, () => ed.ScoreWheelScrollPixels, 12, 96,
                "Pixels moved per mouse-wheel notch over the score page.", "score page mouse wheel scroll", "px"),

            // Copy and paste: one row per paste question that is still asked (Q2..Q5); beats onto notes always insert in the bar (Paste special offers the rest).
            PasteRow(ed, PasteQuestion.Octave, "Pastes between instruments of different range, such as guitar and bass: keep the exact pitch, or shift by an octave automatically.",
                "paste copy octave pitch range guitar bass shift instrument"),
            PasteRow(ed, PasteQuestion.BarsOntoNotes, "Pastes of whole bars onto bars that already have notes: overwrite them, or insert before or after.",
                "paste copy bars overwrite insert before after replace"),
            PasteRow(ed, PasteQuestion.BarSettings, "Whether pasted bars bring their time signature, key, tempo and similar settings, or keep the target bars' own.",
                "paste copy bar settings time signature key tempo keep target"),
            PasteRow(ed, PasteQuestion.Drums, "Pastes between a pitched instrument and a drum track: put the rhythm onto one drum sound, or do not paste.",
                "paste copy drums drum sound rhythm pitched track"),

        };
    }
}
