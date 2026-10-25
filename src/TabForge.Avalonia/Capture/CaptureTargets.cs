using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using TabForge.Avalonia.Platform;
using TabForge.Avalonia.Shell;
using TabForge.Avalonia.Views;

namespace TabForge.Avalonia.Capture;

// Owns: turning a script's target name into the control a shot or frame sequence renders: the main window, a tool window, the open menu, a pane or one named element.
// Does not own: rendering or saving (CaptureImage, ShotSteps), opening tool windows (ToolSteps).
// Tests: TabForge.Avalonia.Tests.CaptureSessionTests.
internal sealed class CaptureTargets(CaptureSession session)
{
    /// <summary>Targets: window (default), window:&lt;Tool&gt;, menu, score, timeline, fretboard, band, tracks, statusbar, titlebar, element:&lt;Name or AutomationId&gt;.</summary>
    public Control Resolve(string? target)
    {
        target ??= "window";
        if (target == "window") return session.Window;
        if (target == "menu") return session.OpenMenu ?? throw new InvalidOperationException("No menu is open: run a menu step first.");
        if (target.StartsWith("window:", StringComparison.Ordinal)) return Tool(target[7..]);
        if (target.StartsWith("element:", StringComparison.Ordinal)) return Element(target[8..]);
        return target switch
        {
            "score" => Viewport(session.Find<ScorePreviewView>()),
            "timeline" => session.Find<ArrangementView>(),
            "fretboard" => session.Find<InstrumentPanelStrip>(),
            "band" => session.Find<BandView>(),
            "tracks" => session.Find<TrackHeaderColumn>(),
            "statusbar" => session.Find<StatusBarView>(),
            "titlebar" => session.Find<TitleBarView>(),
            _ => throw new InvalidOperationException($"Unknown target \"{target}\"."),
        };
    }

    /// <summary>The scroll viewer that shows <paramref name="content"/> (what a user sees of it), or the content itself when it has none: a score page is far taller than the pane.</summary>
    private static Control Viewport(Control content) => content.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault() ?? content;

    private Window Tool(string name)
    {
        if (session.Tools.TryGetValue(name, out var tool)) return tool;
        return session.AllWindows().FirstOrDefault(w => w != session.Window && (w.GetType().Name == name || w.Title == name))
            ?? throw new InvalidOperationException($"No open window named \"{name}\".");
    }

    private Control Element(string name)
    {
        foreach (var root in new Control[] { session.Window }.Concat(session.Tools.Values))
        {
            var hit = root.GetLogicalDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name || global::Avalonia.Automation.AutomationProperties.GetAutomationId(c) == name);
            if (hit is not null) return hit;
        }
        throw new InvalidOperationException($"No element named \"{name}\".");
    }
}
