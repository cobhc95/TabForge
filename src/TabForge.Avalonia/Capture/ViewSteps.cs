using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TabForge.Avalonia.Platform;
using TabForge.Capture;
using TabForge.Models;

namespace TabForge.Avalonia.Capture;

// Owns: the verbs that put the main window in the state a shot shows: open, theme, size, command, track, bar, select, highlight, playhead, panel, zoom, wait.
// Does not own: opening tool windows (ToolSteps), writing images (ShotSteps, FrameSteps), the run loop (CaptureSession).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class ViewSteps(CaptureSession session)
{
    public void Register(Dictionary<string, Func<CaptureStep, Task>> verbs)
    {
        verbs["open"] = Open;
        verbs["theme"] = step => Settle(() => session.ApplyTheme(step.Text == "light"));
        verbs["size"] = step => Settle(() => { var v = step.Numbers; session.Window.Width = v[0]; session.Window.Height = v[1]; });
        verbs["command"] = step => Settle(() => (session.Window.PreferencesActions ?? throw new InvalidOperationException("The window has no command runner.")).RunCommand(step.Text));
        verbs["notation"] = step => Settle(() => session.Window.PreferencesActions!.RunCommand("View.Notation" + step.Text switch { "tab" => "Tab", "staff" => "Staff", _ => "Both" }));
        verbs["track"] = step => Settle(() => session.Document.TrackIndex = step.Int);
        verbs["bar"] = step => Settle(() =>
        {
            var score = session.Find<ScorePreviewView>();
            score.Editor.SetPosition(step.Int - 1, 0, session.Document.CursorString);
            score.RevealBar(step.Int - 1, step.Option("margin", 20));
        });
        verbs["select"] = step => Settle(() => Select(step));
        verbs["clear-selection"] = step => Settle(() => session.Document.Selection.Clear(SelectionOrigin.Command));
        verbs["highlight"] = step => Settle(() => { var v = step.Numbers; session.Document.Highlight.Set((int)v[0], (int)v[1] - 1); });
        verbs["playhead"] = step => Settle(() => Playhead(step));
        verbs["stop"] = step => Settle(() => session.Sessions.For(session.Document)?.Stop());
        verbs["panel"] = step => Settle(() => Panel(step.Text));
        verbs["zoom"] = step => Settle(() => session.Find<ScorePreviewView>().SetZoom(step.Value.GetDouble()));
        verbs["wait"] = step => session.SettleAsync(step.Int);
    }

    private async Task Settle(Action change)
    {
        change();
        await session.SettleAsync();
    }

    private async Task Open(CaptureStep step)
    {
        if (step.Text == "demo") return;   // the run starts on the Ashen Meridian demo song
        if (!await session.Window.OpenStartupFile(System.IO.Path.GetFullPath(step.Text))) throw new InvalidOperationException("The song did not open: " + step.Text);
        await session.SettleAsync(CaptureSession.SettleMs * 2);
    }

    /// <summary>Selects bars (1-based, as shown): a timeline range over all tracks, or with "scope":"track" a score selection of the current track.</summary>
    private void Select(CaptureStep step)
    {
        var range = step.Numbers;
        var scope = step.Option("scope", "all") == "track" ? SelectionScope.ThisTrack : SelectionScope.AllTracks;
        session.Document.Selection.SetRange(session.Document.TrackIndex, (int)range[0] - 1, (int)range[1] - 1, SelectionOrigin.Timeline, 0, -1, scope);
        if (scope == SelectionScope.ThisTrack) session.Find<ScorePreviewView>().SelectBars((int)range[0] - 1, (int)range[1] - 1);
    }

    /// <summary>Draws the playing views at performed bar p[0] (1-based, repeats counted) and fraction p[1] of it.</summary>
    private void Playhead(CaptureStep step)
    {
        var moment = step.Numbers;
        session.HoldMoment((int)moment[0] - 1, moment.Length > 1 ? moment[1] : 0);
    }

    private void Panel(string name)
    {
        var hide = name.StartsWith("hide:", StringComparison.Ordinal);
        session.Window.DockWorkspace.SetPanelVisible(hide ? name[5..] : name, !hide);
    }
}
