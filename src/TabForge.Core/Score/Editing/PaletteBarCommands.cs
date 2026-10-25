using TabForge.Documents;
using TabForge.Models;

namespace TabForge.ScoreEditing;

// Owns: the palette tools that flip one bar property (free time, double barline, swing feel, simile repeat, system breaks, gray inactive voice)
//   as one undo step, and the status line each one reports.
// Does not own: the flag edits (MeasureProperties), the buttons (the WPF and Avalonia palettes) or the redraw after the edit.
// Tests: no dedicated test class.
/// <summary>What a palette bar tool did: whether the song changed and the status line to show (empty when it changed nothing).</summary>
public readonly record struct PaletteBarResult(bool Changed, string Status);

/// <summary>The bar-property palette tools as edits of one document.</summary>
public static class PaletteBarCommands
{
    /// <summary>Marks the bar as a free-time (cadenza) bar, or clears the mark.</summary>
    public static PaletteBarResult FreeTime(IEditableDocument document, int trackRow, int bar)
    {
        var value = false;
        var changed = DocumentEdits.Run(document, project => MeasureProperties.TryToggle(project, trackRow, bar, measure => measure.FreeTime, (measure, on) => measure.FreeTime = on, out value)).Changed;
        return changed ? new(true, value ? "Free-time measure on" : "Free-time measure off") : default;
    }

    public static PaletteBarResult DoubleBarline(IEditableDocument document, int trackRow, int bar) =>
        new(DocumentEdits.Run(document, project => MeasureProperties.TryToggle(project, trackRow, bar, measure => measure.IsDoubleBar, (measure, on) => measure.IsDoubleBar = on, out _)).Changed, "");

    public static PaletteBarResult TripletFeel(IEditableDocument document, int trackRow, int bar)
    {
        var next = "";
        var changed = DocumentEdits.Run(document, project => MeasureProperties.TryCycleTripletFeel(project, trackRow, bar, out next)).Changed;
        return changed ? new(true, next switch { "None" => "Straight feel", "Triplet8th" => "Eighth-note swing", _ => "Sixteenth-note swing" }) : default;
    }

    /// <summary>One-bar (1) or two-bar (2) repeat mark.</summary>
    public static PaletteBarResult Simile(IEditableDocument document, int trackRow, int bar, int barCount)
    {
        var enabled = false;
        var changed = DocumentEdits.Run(document, project => MeasureProperties.TryToggleSimile(project, trackRow, bar, barCount, out enabled)).Changed;
        return changed ? new(true, enabled ? $"Repeating previous {barCount} bar{(barCount == 1 ? "" : "s")}" : "Simile repeat removed") : default;
    }

    /// <summary>Forces (<paramref name="force"/>) or prevents a system break before the bar.</summary>
    public static PaletteBarResult LineBreak(IEditableDocument document, int trackRow, int bar, bool force)
    {
        var enabled = false;
        var changed = DocumentEdits.Run(document, project => MeasureProperties.TryToggleLineBreak(project, trackRow, bar, force, out enabled)).Changed;
        if (!changed) return default;
        return new(true, force
            ? enabled ? "System break forced before this measure" : "Forced system break removed"
            : enabled ? "Automatic system break prevented before this measure" : "System-break prevention removed");
    }

    /// <summary>Dims the inactive voice, or shows it at normal brightness.</summary>
    public static PaletteBarResult InactiveVoiceGray(IEditableDocument document)
    {
        var gray = false;
        DocumentEdits.Run(document, project => { gray = project.GrayInactiveVoice = !project.GrayInactiveVoice; return true; });
        return new(true, gray ? "Inactive voice dimmed" : "Inactive voice at normal brightness");
    }
}
