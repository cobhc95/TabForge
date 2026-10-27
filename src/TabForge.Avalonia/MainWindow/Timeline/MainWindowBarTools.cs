using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the Structure, Rhythm and Layout tools that ask a question first (Check bars, time and key signature, directions, add section, N-tuplet,
// sound duration, tempo change) and the Marker list tool: each asks with the wording, then hands the answer to the score editor.
// Does not own: the questions' windows (BarDialogs, TextPromptWindow), the edits (Core ScorePreviewEditor) or the tools that need no question (MainWindowEdit).
// Deviation: the Sections pane is a fixed sample, so the Marker list tool only brings it into view and an added marker shows there after a restart.
// Tests: TabForge.Avalonia.Tests.NotificationBarTests.
/// <summary>Registers the question tools of the palette and the Bar and Sections menus.</summary>
public static class MainWindowBarTools
{
    /// <summary>The command ids registered here.</summary>
    public static readonly IReadOnlyList<string> Commands = new[]
    {
        "Bar.Check", "Bar.TimeSignature", "Bar.KeySignature", "Bar.Directions", "Section.Add",
        "Tool.gp:custom_ntuplet", "Tool.gp:sound_duration", "Tool.composition:tempo", "Tool.gp:marker_list",
    };

    private const int MaxIssueLines = 20;

    /// <param name="afterInsert">Called after Section.Add landed (the shared insert refresh); null does nothing more.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window owner, Action? showMarkerList, Action? afterInsert = null)
    {
        void Say(string text) => score.Editor.Notice?.Invoke(text);
        void Ask(string id, Func<Task> run) => commands.Add(id, () => UiTasks.Forget(run(), id, Say));
        Ask("Bar.Check", () => CheckBars(score, owner));
        Ask("Bar.TimeSignature", () => TimeSignature(score, owner));
        Ask("Bar.KeySignature", () => KeySignature(score, owner));
        Ask("Bar.Directions", () => Directions(score, owner));
        Ask("Section.Add", async () => { if (await AddSection(score, owner)) afterInsert?.Invoke(); });
        Ask("Tool.gp:custom_ntuplet", () => Tuplet(score, owner));
        Ask("Tool.gp:sound_duration", () => SoundDuration(score, owner));
        Ask("Tool.composition:tempo", () => TempoChange(score, owner));
        commands.Add("Tool.gp:marker_list", () =>
        {
            showMarkerList?.Invoke();
            Say(score.Editor.Project!.Markers.Count == 0 ? "No markers yet" : "Marker list focused");
        });
    }

    // Check bars (F4): the same rule as the red bar tint; the result goes to the top bar (Info), never modal: a clean song says so,
    // else the first bars that do not add up (the bar's tooltip holds the full list).
    private static Task CheckBars(ScorePreviewView score, Window owner)
    {
        _ = owner;
        var issues = score.Editor.BarProblems();
        if (issues.Count == 0)
        {
            score.Editor.Notice?.Invoke("Every bar adds up to its time signature.");
            return Task.CompletedTask;
        }
        var more = issues.Count > MaxIssueLines ? $"\n…and {issues.Count - MaxIssueLines} more" : "";
        score.Editor.Notice?.Invoke(string.Join("\n", issues.Take(MaxIssueLines)) + more);
        return Task.CompletedTask;
    }

    private static string? SelectedBarsText(ScorePreviewView score) =>
        score.Editor.SelectedBarRange() is { } range ? $"bars {range.First + 1}-{range.Last + 1}" : null;

    private static async Task TimeSignature(ScorePreviewView score, Window owner)
    {
        var project = score.Editor.Project!;
        var bar = score.Editor.CurrentBar;
        var answer = await DialogLauncher.Ask<TimeSignatureWindow, TimeSignatureAnswer?>(owner, () => new TimeSignatureWindow(bar?.TimeSigNum ?? project.TimeSignatureNumerator, bar?.TimeSigDenom ?? project.TimeSignatureDenominator, SelectedBarsText(score)));
        if (answer is not null) score.Edit(editor => editor.SetTimeSignature(answer.Numerator, answer.Denominator, answer.OnlyThisBar));
    }

    private static async Task KeySignature(ScorePreviewView score, Window owner)
    {
        var project = score.Editor.Project!;
        var bar = score.Editor.CurrentBar;
        var answer = await DialogLauncher.Ask<KeySignatureWindow, KeySignatureAnswer?>(owner, () => new KeySignatureWindow(bar?.KeySignature ?? project.KeySignature, bar?.KeySignatureMinor ?? project.KeySignatureMinor, SelectedBarsText(score)));
        if (answer is not null) score.Edit(editor => editor.SetKeySignature(answer.Signature, answer.Minor, answer.OnlyThisBar));
    }

    private static async Task Directions(ScorePreviewView score, Window owner)
    {
        if (score.Editor.CurrentBar is not { } bar) return;
        var answer = await DialogLauncher.Ask<DirectionsWindow, DirectionsAnswer?>(owner, () => new DirectionsWindow(bar.Directions, bar.AlternateEnding));
        if (answer is not null) score.Edit(editor => editor.SetDirections(answer.Directions, answer.Ending));
    }

    // One section per bar: a marker already at the cursor bar is edited, else a new one is added there.
    private static async Task<bool> AddSection(ScorePreviewView score, Window owner)
    {
        var existing = score.Editor.MarkerAtCursor;
        var bar = Math.Clamp(score.Editor.SelectedMeasure, 0, Math.Max(0, score.Editor.BarCount - 1));
        var answer = await DialogLauncher.Ask<MarkerWindow, MarkerAnswer?>(owner, () => new MarkerWindow(existing is null ? "Add" : "Save", existing?.Title ?? "Section", existing?.ColorHex ?? MainWindowTimelineSections.NewSectionColour, bar));
        return answer is not null && score.Edit(editor => editor.AddOrEditMarker(answer.Title, answer.ColourHex));
    }

    private static async Task Tuplet(ScorePreviewView score, Window owner)
    {
        var (numerator, denominator) = score.Editor.CurrentTuplet;
        var numeratorText = await Prompt(owner, "Tuplet", "Notes in the tuplet (numerator):", numerator);
        if (!int.TryParse(numeratorText, out var notes)) return;
        var denominatorText = await Prompt(owner, "Tuplet", "Normal note value (denominator):", denominator);
        if (!int.TryParse(denominatorText, out var normal)) return;
        score.Edit(editor => editor.SetTuplet(notes, normal));
    }

    private static async Task SoundDuration(ScorePreviewView score, Window owner)
    {
        if (score.Editor.SoundDurationPercent is not { } current) return;
        var text = await Prompt(owner, "Sound duration", "Sounding duration (% of written value, 1–200):", current);
        if (int.TryParse(text, out var percent)) score.Edit(editor => editor.SetSoundDuration(percent));
    }

    private static async Task TempoChange(ScorePreviewView score, Window owner)
    {
        if (score.Editor.CurrentBar is null) return;
        var text = await Prompt(owner, "Tempo change", "Tempo in beats per minute (20–400):", score.Editor.TempoAtCursor);
        if (int.TryParse(text, out var tempo)) score.Edit(editor => editor.SetTempoChange(tempo));
    }

    private static Task<string?> Prompt(Window owner, string title, string label, int initial) =>
        DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(title, label, initial.ToString()));
}
