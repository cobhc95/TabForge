using System.Collections.Generic;
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the track row menu's Convert to instrument track row on an audio track (ContextMenuHost.ClickedTrack): the instrument picker, then the
//   clip question (or the remembered answer, which it saves when ticked), then the one-step conversion.
// Does not own: the conversion and its bar lookup (AudioToInstrumentConversion), the clip question texts (PromptTextsPresenter), the menu rows
//   (TrackRowMenus) and the window refresh after the edit.
// Tests: MainWindowMenuTests (every menu row's command is handled).
public static class MainWindowConvertToInstrument
{
    /// <summary>The command ids this class handles: the track row menu's Convert to instrument track row.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { TrackRowMenus.Convert };

    /// <param name="songs">The tabs; the command acts on the shown song.</param>
    /// <param name="owner">The window the picker and the question open over.</param>
    /// <param name="rows">The host of the track row menus; its ClickedTrack is the track the row acts on.</param>
    /// <param name="settings">The current settings; the remembered clip answer is read and written here.</param>
    /// <param name="saveSettings">Saves the settings after a remembered answer is ticked.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, Window owner, ContextMenuHost rows, Func<AppSettings> settings, Action saveSettings) =>
        commands.Add(TrackRowMenus.Convert, () => _ = ConvertAsync(songs.Active, owner, rows.ClickedTrack, settings(), saveSettings));

    private static async Task ConvertAsync(IEditableDocument document, Window owner, int index, AppSettings appSettings, Action saveSettings)
    {
        var tracks = document.Project.Tracks;
        if (index < 0 || index >= tracks.Count || tracks[index] is not { IsAudio: true } audio) return;
        var picked = await DialogLauncher.Ask<InstrumentPickerWindow, string?>(owner, () => new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), null));
        if (picked is null) return;
        var editing = appSettings.Editing;
        var hasMidi = audio.AudioClips.Any(clip => clip.IsMidi);
        var writeNotation = false;
        if (audio.AudioClips.Count > 0)
        {
            if (hasMidi && editing.ConvertMidiClips is AudioToInstrumentConversion.WriteAnswer or AudioToInstrumentConversion.KeepAnswer)
                writeNotation = editing.ConvertMidiClips == AudioToInstrumentConversion.WriteAnswer;
            else
            {
                var outcome = await PromptWindow.AskAsync(owner, PromptTextsPresenter.ConvertTrackToInstrumentClips(audio.Name, hasMidi));
                var (answer, remember) = PromptTextsPresenter.ConvertClipsAnswer(outcome.Answer, hasMidi, outcome.SelectedChoice, outcome.RememberTicked);
                if (answer == ClipAnswer.Cancel) return;
                writeNotation = hasMidi && answer == ClipAnswer.WriteNotation;
                if (hasMidi && remember)
                {
                    editing.ConvertMidiClips = writeNotation ? AudioToInstrumentConversion.WriteAnswer : AudioToInstrumentConversion.KeepAnswer;
                    saveSettings();
                }
            }
        }
        Func<double, (int Bar, double Fraction)>? barAt = writeNotation ? sec => AudioToInstrumentConversion.BarAtSongSec(document.Project, sec) : null;
        AudioToInstrumentConversion.ConvertAudioToInstrument(document, audio, InstrumentNaming.WithoutStringCount(picked), barAt);
    }
}
