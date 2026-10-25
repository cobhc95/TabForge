using System.Linq;
using TabForge.Documents;
using TabForge.Plugins;
using TabForge.Services;

namespace TabForge.Avalonia.Views.Fx;

// Owns: putting the saved default chains onto a song as it opens: a track without plug-ins takes the default chain of its instrument ("Auto-load for this
//   instrument") and every "Add as a track on startup" chain is added as a track that is not armed. The song keeps its clean state.
// Does not own: the saved chains and their rules (AutoChains, StartupTracks) or the tabs (MainWindowTabs, which calls this when it adds a song).
// Tests: no dedicated test class.
public static class FxDefaultChains
{
    /// <summary>Applies both defaults to the song; true when it changed. A song that was clean stays clean (the startup tracks are not saved with it).</summary>
    public static bool Apply(PluginSettings settings, SongDocument song)
    {
        if (settings.AutoChains.Count == 0 && settings.StartupTracks.Count == 0) return false;
        var wasClean = !song.HasUnsavedChanges;
        var before = song.Project.Tracks.Sum(t => t.Rig.Plugins.Count);
        AutoChains.Apply(settings, song.Project);
        var added = StartupTracks.Apply(settings, song.Project);
        var changed = added || song.Project.Tracks.Sum(t => t.Rig.Plugins.Count) != before;
        if (wasClean) song.MarkClean();
        return changed;
    }
}
