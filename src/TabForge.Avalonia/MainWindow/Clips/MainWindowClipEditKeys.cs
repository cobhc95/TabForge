using System;
using System.Collections.Generic;
using System.Linq;
using TabForge.Avalonia.Platform;
using TabForge.Features.Recording.Model;
using TabForge.Models;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the clip commands that change the selected clip's mute, fades or pieces: Clip.Mute and Clip.FadeReset (on every selected clip) and Clip.Glue (the first selected clip). Each is one
//   DocumentEdits step on the shown song, and a key is used only while a clip is selected (the same guard as MainWindowClipKeys).
// Does not own: the other clip commands (MainWindowClipKeys), the edit rules (AudioClipEdits, TakeGroupEdits and ClipSplitGlue in TabForge.Core), the key table
//   (MainWindowKeyboard). Clip.Split and Clip.Properties: MainWindowClipProperties.
// Tests: TabForge.Avalonia.Tests.AudioClipEditsTests, TabForge.Avalonia.Tests.MainWindowMenuTests (every Commands id is handled).
public static class MainWindowClipEditKeys
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Clip.Mute", "Clip.FadeReset", "Clip.Glue" };

    /// <param name="commands">The window's command table; the commands are added with the selected-clip guard.</param>
    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="panes">The latest arrangement pane, whose timeline holds the clip selection.</param>
    /// <param name="notice">Shows a short notice when a take-group edit is refused.</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, Action<string>? notice = null)
    {
        MainWindowClipKeys.Add(commands, songs, panes, "Clip.Mute",
            target => MainWindowClipKeys.Edit(target, () => ToggleMute(target)));
        MainWindowClipKeys.Add(commands, songs, panes, "Clip.FadeReset",
            target => MainWindowClipKeys.Edit(target, () => MainWindowClipKeys.Each(target, c => AudioClipEdits.ResetFades(c.Clip))));
        MainWindowClipKeys.Add(commands, songs, panes, "Clip.Glue", target => Glue(target, notice));
    }

    /// <summary>Mutes every selected clip, or unmutes them all when none is audible; one step. A take group flips as a whole.</summary>
    internal static bool ToggleMute(MainWindowClipKeys.Target target)
    {
        var mute = target.All.Any(c => !c.Clip.Muted);
        return MainWindowClipKeys.Each(target, c => c.Clip.Muted != mute && TakeGroupEdits.ToggleMute(c.Track, c.Clip));
    }

    /// <summary>Glues the selected clip with the clips that continue it on its lane; the glued clip becomes the selection. Nothing to glue takes no
    /// step, and clips from different passes never glue together (a pass keeps its group and number).</summary>
    private static void Glue(MainWindowClipKeys.Target target, Action<string>? notice)
    {
        var track = target.Selection.Track;
        var clip = target.Selection.Clip;
        var chain = ClipSplitGlue.Chain(track, clip);
        if (chain.Any(other => other.TakeGroupId != clip.TakeGroupId || other.TakeGroupPass != clip.TakeGroupPass)) return;
        MainWindowClipKeys.Edit(target, () =>
        {
            if (chain.Count < 2) return false;
            if (ClipSplitGlue.Glue(track, clip) is not { } glued) return false;
            target.Timeline.SelectedClip = new TimelineClipSelection(track, glued);
            return true;
        });
    }
}
