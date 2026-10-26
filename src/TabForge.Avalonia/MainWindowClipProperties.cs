using System;
using System.Collections.Generic;
using Avalonia.Controls;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Views;
using TabForge.Avalonia.Views.Dialogs;
using TabForge.Features.Recording.Rules;
using TabForge.Models;
using TabForge.Presenters;
using TabForge.Services;

namespace TabForge.Avalonia;

// Owns: the clip commands Clip.Split (splits the selected clip under the playhead; the second part becomes the selection) and
//   Clip.Properties (the clip properties dialog). Each is one DocumentEdits step on the shown song; a dialog closed with nothing changed
//   takes no step. Keys are used only while a clip is selected (MainWindowClipKeys.Add).
// Does not own: the split rule (ClipSplitGlue in TabForge.Core), the dialog's wording and apply rule (ClipPropertiesPresenter), the
// dialog controls (ClipPropertiesWindow), the other clip commands (MainWindowClipKeys, MainWindowClipEditKeys). The lane edit cursor
// is not ported: Split uses the playhead, the fallback.
// Tests: TabForge.Avalonia.Tests.ClipPropertiesTests, TabForge.Avalonia.Tests.MainWindowMenuTests (every Commands id is handled).
public static class MainWindowClipProperties
{
    /// <summary>The command ids this class handles.</summary>
    public static readonly IReadOnlyList<string> Commands = new[] { "Clip.Split", "Clip.Properties" };

    /// <summary>The notice when the playhead is not inside the selected clip.</summary>
    public const string SplitOutsideNotice = "Put the edit cursor (click the clip) or the playhead inside the clip to split it";

    /// <param name="commands">The window's command table.</param>
    /// <param name="songs">The tabs; the commands act on the shown song.</param>
    /// <param name="panes">The latest arrangement pane, whose timeline holds the clip selection and the playhead.</param>
    /// <param name="owner">The window that owns the properties dialog.</param>
    /// <param name="notice">Shows a short notice (the split outside the clip).</param>
    public static void Register(MainWindowCommands commands, MainWindowTabs songs, MainWindowDocumentPanes panes, Window owner, Action<string> notice)
    {
        MainWindowClipKeys.Add(commands, songs, panes, "Clip.Split", target => Split(target, notice));
        MainWindowClipKeys.Add(commands, songs, panes, "Clip.Properties", target => _ = EditProperties(target, owner));
    }

    /// <summary>Splits the selected clip at the lane edit cursor when it lies inside the clip, else at the playhead; outside the
    /// clip it shows the notice and takes no step. The split point becomes the new lane edit cursor.</summary>
    internal static void Split(MainWindowClipKeys.Target target, Action<string> notice)
    {
        var (track, clip) = (target.Selection.Track, target.Selection.Clip);
        var project = target.Document.Project;
        var performed = SongExtent.Timing(project).Performed;
        var playheadAt = new RepeatPosition(target.Timeline.PlayheadBar, target.Timeline.PlayheadFraction, target.Timeline.PlayheadPass);
        var playheadSec = PerformedAt(project, performed, playheadAt);
        RepeatPosition? splitAt = playheadAt;
        // The lane cursor is preferred when it lies inside the clip, exactly as before; its pass-aware position (when the click resolved
        // one) supplies the performed second instead of the bar's first occurrence.
        var cursorSec = double.NaN;
        if (target.Timeline.LaneCursor is { } cursor && ReferenceEquals(cursor.Track, track) && cursor.Lane == clip.Lane)
        {
            cursorSec = cursor.Position is { } at ? PerformedAt(project, performed, at) : cursor.Sec;
            if (double.IsNaN(cursorSec)) cursorSec = cursor.Sec;
            if (ClipSplitGlue.CanSplit(clip, cursorSec)) splitAt = cursor.Position;
        }
        var sec = ClipSplitGlue.CanSplit(clip, cursorSec) ? cursorSec : playheadSec;
        if (!ClipSplitGlue.CanSplit(clip, sec)) { notice(SplitOutsideNotice); return; }
        MainWindowClipKeys.Edit(target, () =>
        {
            if (ClipSplitGlue.Split(track, clip, sec) is not { } second) return false;
            target.Timeline.SelectedClip = new TimelineClipSelection(track, second);
            target.Timeline.LaneCursor = null;   // the split consumes the split marker
            return true;
        });
    }

    /// <summary>The performed second of a pass-aware position; a play that does not exist falls back to the old first-occurrence bar second.</summary>
    private static double PerformedAt(SongProject project, PerformedBar[] performed, RepeatPosition position)
    {
        var sec = RepeatMap.ToPerformed(performed, position);
        return double.IsNaN(sec) ? AudioClipTimelineRules.SecondsAtBar(project, position.WrittenBar, position.Fraction) : sec;
    }

    /// <summary>Opens the dialog on a copy of the selected clip; OK applies the copy's values as one step.</summary>
    private static async System.Threading.Tasks.Task EditProperties(MainWindowClipKeys.Target target, Window owner)
    {
        var edited = target.Selection.Clip.Clone();
        if (await DialogLauncher.Ask<ClipPropertiesWindow, bool>(owner, () => new ClipPropertiesWindow(edited))) Apply(target, edited);
    }

    /// <summary>Copies the edited name, volume, pitch, speed and mute onto the selected clip as one undo step; equal values take no step.</summary>
    internal static void Apply(MainWindowClipKeys.Target target, AudioClip edited)
    {
        var clip = target.Selection.Clip;
        MainWindowClipKeys.Edit(target, () =>
        {
            var before = (clip.Name, clip.GainDb, clip.Pitch, clip.Speed, clip.Muted);
            ClipPropertiesPresenter.Apply(clip, edited.Name, edited.GainDb, edited.Pitch, edited.Speed, edited.Muted);
            return before != (clip.Name, clip.GainDb, clip.Pitch, clip.Speed, clip.Muted);
        });
    }
}
