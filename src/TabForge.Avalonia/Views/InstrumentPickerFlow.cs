using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Transport;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Documents;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia.Views;

// Owns: one Choose instrument run on a MIDI track, the same for every entry point (the track row icon and the Track properties button): the picker
//   over its owner with the track's family open and its tile selected, every tile click sounding live (InstrumentChoice over the song's engine),
//   Select as one undo step, Cancel and the window's close box as an exact restore.
// Does not own: the choice and its rules (InstrumentChoice), the picker window (InstrumentPickerWindow), the engine (EngineProgramSink).
// Tests: TabForge.Avalonia.Tests.InstrumentChoiceTests.
// Threads: UI thread only.
public sealed class InstrumentPickerFlow
{
    private readonly AvaloniaPlaybackSessions? _playback;
    private readonly Func<PluginSettings> _plugins;

    /// <summary>Raised when a run ends (Select, Cancel or close): the instrument of a track may have changed, so every view of it refreshes in full.</summary>
    public event Action? InstrumentChanged;

    /// <summary>Raised after every live preview (each hover or click on a tile): only the instrument panel needs the cheap refresh; the full refresh waits for <see cref="InstrumentChanged"/>.</summary>
    public event Action? InstrumentPreviewed;

    /// <param name="playback">The sessions whose engine sounds the choice; null runs a silent choice.</param>
    /// <param name="plugins">The plug-in settings an engine sync reads.</param>
    public InstrumentPickerFlow(AvaloniaPlaybackSessions? playback = null, Func<PluginSettings>? plugins = null)
    {
        _playback = playback;
        _plugins = plugins ?? (() => new PluginSettings());
    }

    /// <summary>Runs the picker for <paramref name="track"/> over <paramref name="owner"/>. <paramref name="document"/> holds the track (Select is one undo step
    /// and the song's engine follows); null for a track that is not in a song yet (Select keeps the previewed sound, no step). <paramref name="previewed"/> runs
    /// after every preview and <paramref name="finished"/> once when the run ends (Select, Cancel or close), so the caller repaints. False for an audio track or when nothing was kept.</summary>
    public async Task<bool> ChooseAsync(Window owner, IEditableDocument? document, SongProject project, TrackModel track, Action? previewed = null, Action? finished = null)
    {
        if (track.IsAudio) return false;
        var sink = _playback is not null && document is SongDocument song ? new EngineProgramSink(_playback, song, _plugins) : null;
        var choice = new InstrumentChoice(project, track, sink);
        choice.Changed += () => { previewed?.Invoke(); InstrumentPreviewed?.Invoke(); };
        var picked = await DialogLauncher.Ask<InstrumentPickerWindow, string?>(owner,
            () => new InstrumentPickerWindow(InstrumentPickerWindow.CatalogueEntries(), choice.Current?.Name ?? track.InstrumentName, focusFamily: true,
                onPreview: choice.Preview, routeNote: choice.RouteNote));
        var kept = false;
        try
        {
            if (picked is not null && choice.RemapsNotes(picked) && !await ConfirmRemapAsync(owner, picked)) picked = null;
            kept = picked is not null && choice.Commit(document, picked).Changed;
        }
        finally
        {
            if (!kept) choice.Cancel();   // a close, a refusal or a failed dialog restores the track and its sound
            finished?.Invoke();
            InstrumentChanged?.Invoke();
        }
        return kept;
    }

    private static async Task<bool> ConfirmRemapAsync(Window owner, string chosen)
    {
        var text = AudioToInstrumentConversion.IsDrumSound(chosen)
            ? "Switching to drums remaps the notes to drum sounds. Continue?"
            : "Switching to a pitched instrument remaps the drum notes to strings and frets. Continue?";
        return await AvaloniaMessageBox.Show(new AvaloniaDialogHost(), owner, text, "Change instrument", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxAnswer.No) == MessageBoxAnswer.Yes;
    }
}
