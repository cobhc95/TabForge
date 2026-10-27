using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the two Tools menu rows that change the score's notes and the note sound: Transpose (asks for the semitones, then moves the shown
//   track's notes over the selection or the whole track) and Preview note sound (flips the saved audio setting and its menu check).
// Does not own: the rows (MainMenuTable in Core), the commands table (MainWindowCommands), the transpose rule (MusicTheoryService and
//   ScorePreviewEditor.TransposeShownTrack in Core), the question window (TextPromptWindow), the settings file (MainWindowSettings).
// Tests: TabForge.Avalonia.Tests.MainWindowMenuTests (every menu row is handled or disabled).
/// <summary>Registers the Transpose and Preview note sound rows of the Tools menu.</summary>
public static class MainWindowToolsMenu
{
    private const string TransposeId = "Tools.Transpose";
    private const string PreviewNotesId = "Tools.PreviewNotes";
    private const string TransposeTitle = "Transpose";
    private const string PreviewNotesOnText = "Note preview on";
    private const string PreviewNotesOffText = "Note preview off";

    /// <summary>The preview choice when there is no settings file (a session without one): on, the app default.</summary>
    private const bool PreviewNotesWithoutSettings = true;

    /// <summary>The Tools menu command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { TransposeId, PreviewNotesId };

    /// <param name="commands">The commands table the two rows run from.</param>
    /// <param name="score">The score view whose shown track is transposed.</param>
    /// <param name="owner">The window whose Tools menu row shows the note preview check.</param>
    /// <param name="settings">The shared settings the note preview choice is read from and saved to; null keeps the choice for this session.</param>
    public static void Register(MainWindowCommands commands, ScorePreviewView score, Window owner, MainWindowSettings? settings)
    {
        commands.Add(TransposeId, () => UiTasks.Forget(TransposeShownTrack(score, owner), TransposeId, text => score.Editor.Notice?.Invoke(text)));
        commands.Add(PreviewNotesId, () => TogglePreviewNotes(score, owner, settings));
        owner.Opened += (_, _) => SetCheck(owner, PreviewNotesId, PreviewNotesOn(settings));
    }

    /// <summary>Tools > Transpose: asks for the semitones (the question names the selection when there is one), then applies them.</summary>
    private static async Task TransposeShownTrack(ScorePreviewView score, Window owner)
    {
        var max = MusicTheoryService.MaxTransposeSemitones;
        var scope = score.Editor.HasSelection ? "the selection" : "the whole track";
        var text = await DialogLauncher.Ask<TextPromptWindow, string?>(owner, () => new TextPromptWindow(TransposeTitle, $"Semitones (-{max}..{max}) for {scope}:", "0"));
        if (text is not null && int.TryParse(text, out var semitones)) score.Edit(editor => editor.TransposeShownTrack(semitones));
    }

    /// <summary>Tools > Preview note sound: flips the choice, saves it in the audio settings and sets the menu check.</summary>
    private static void TogglePreviewNotes(ScorePreviewView score, Window owner, MainWindowSettings? settings)
    {
        var on = !PreviewNotesOn(settings);
        if (settings is not null)
        {
            settings.Settings.Audio.PreviewNotes = on;
            settings.SaveSettings();
        }
        SetCheck(owner, PreviewNotesId, on);
        score.Editor.Notice?.Invoke(on ? PreviewNotesOnText : PreviewNotesOffText);
    }

    private static bool PreviewNotesOn(MainWindowSettings? settings) => settings?.Settings.Audio.PreviewNotes ?? PreviewNotesWithoutSettings;

    /// <summary>Sets the check of every Tools menu row whose tag is <paramref name="id"/>.</summary>
    private static void SetCheck(Window owner, string id, bool on)
    {
        foreach (var item in owner.GetLogicalDescendants().OfType<MenuItem>().Where(item => Equals(item.Tag, id)))
            item.IsChecked = on;
    }
}
