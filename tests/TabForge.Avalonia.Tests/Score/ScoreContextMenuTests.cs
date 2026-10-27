using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using TabForge.Avalonia.Platform;
using TabForge.Documents;
using TabForge.Menus;
using TabForge.Presets;
using TabForge.Services;
using TabForge.Views.Score;
using Xunit;

namespace TabForge.Avalonia.Tests;

// Owns: the score's right-click rows: a note's menu (Duration, Dynamics, Effects, Beat, Pitch and string), the cursor a right-click on a note
//   moves to, and the Paste rows over a beat while the clipboard can paste.
// Does not own: the rows (Core ScoreNoteMenus, ScoreMenus), the menu items (ContextMenuBuilder), the hit rule (Core ScoreHitRule).
// Tests: ScoreContextMenuTests (this file).
public class ScoreContextMenuTests
{
    private const double ScanWidth = 1300, ScanHeight = 1800;

    /// <summary>A score in a window of its own; the view is at 100%, so view and page coordinates agree.</summary>
    private static (ScorePreviewView Score, Window Window) Show()
    {
        var score = new ScorePreviewView();
        score.Show(new SongDocument(() => null) { Project = FullDemoSongFactory.Create() });
        var window = new Window { Width = ScanWidth, Height = ScanHeight, Content = score };
        window.Show();
        score.SetZoom(1);
        return (score, window);
    }

    /// <summary>The first point (view coordinates) that the editor's right-click rule reads as a beat matching <paramref name="wanted"/>.</summary>
    private static Point FirstPointWhere(ScorePreviewView score, Func<ScoreContextHit, bool> wanted)
    {
        for (var y = 40.0; y < ScanHeight; y += 4)
            for (var x = 120.0; x < ScanWidth - 20; x += 4)
                if (wanted(score.Editor.ContextHitAt(x, y))) return new Point(x, y);
        throw new InvalidOperationException("The demo score has no point matching the request.");
    }

    [AvaloniaFact]
    public void RightClickInsideTheLoopTintOffersClearLoopOnTopAndOutsideDoesNot()
    {
        var (score, window) = Show();
        var at = FirstPointWhere(score, hit => hit.OverBeat && !hit.OnNote);
        var hit = score.Editor.ContextHitAt(at.X, at.Y);
        Assert.DoesNotContain(score.ContextRowsAt(at), row => row.Id == "Transport.ClearLoopArea");
        score.Editor.ShowLoopTint((hit.Measure, 0, hit.Measure, 1000));
        var rows = score.ContextRowsAt(at).ToList();
        Assert.Equal("Transport.ClearLoopArea", rows[0].Id);
        Assert.Equal("Clear loop", rows[0].Header);
        score.Editor.ShowLoopTint(null);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickOnANoteOffersTheNoteToolGroups()
    {
        var (score, window) = Show();
        var at = FirstPointWhere(score, hit => hit.OnNote);

        var rows = score.ContextRowsAt(at).ToList();

        var headers = rows.Select(row => row.Header).ToList();
        Assert.Contains(ScoreNoteMenus.DurationGroup, headers);
        Assert.Contains(ScoreNoteMenus.DynamicsGroup, headers);
        Assert.Contains(ScoreNoteMenus.EffectsGroup, headers);
        Assert.Contains(ScoreNoteMenus.BeatGroup, headers);
        Assert.Contains(ScoreNoteMenus.PitchAndStringHeader, headers);
        Assert.DoesNotContain(ScoreMenus.Settings, headers);
        var duration = rows.Single(row => row.Header == ScoreNoteMenus.DurationGroup);
        Assert.Contains(duration.Children!, child => child.Header == "Quarter note" && child.Radio);
        window.Close();
    }

    [AvaloniaFact]
    public void RightClickOnANoteMovesTheCursorToThatBeatThroughTheWindowEvents()
    {
        var (score, window) = Show();
        var at = FirstPointWhere(score, hit => hit.OnNote);
        var expected = score.Editor.ContextHitAt(at.X, at.Y);
        var inWindow = score.TranslatePoint(at, window)!.Value;

        window.MouseDown(inWindow, MouseButton.Right);
        window.MouseUp(inWindow, MouseButton.Right);

        Assert.Equal((expected.Measure, expected.Cell, expected.String), score.Probe.Cursor);
        window.Close();
    }

    [AvaloniaFact]
    public void PasteRowsAreOfferedOverABeatOnlyWhileTheClipboardCanPaste()
    {
        var (score, window) = Show();
        var beat = FirstPointWhere(score, hit => hit.OverBeat && !hit.OnNote);
        var clipboard = new ClipboardService(null);
        clipboard.CopyTrack(score.Editor.Track!);

        score.ContextMenus.Clipboard = clipboard;
        var withClip = score.ContextRowsAt(beat).Select(row => row.Id).ToList();
        score.ContextMenus.Clipboard = null;
        var withoutClip = score.ContextRowsAt(beat).Select(row => row.Id).ToList();

        Assert.Contains(ScoreMenus.PasteId, withClip);
        Assert.Contains(ScoreMenus.PasteSpecialId, withClip);
        Assert.DoesNotContain(ScoreMenus.PasteId, withoutClip);
        window.Close();
    }
}
